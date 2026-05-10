using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using MessagePack;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem
{
    public class LiteDbCacheStore : IDisposable, ICacheStore
    {
        private readonly LiteDatabase _db;
        private DateTime _lastFileCleanup = DateTime.UtcNow;
        private static readonly TimeSpan FileCleanupInterval = TimeSpan.FromHours(1);

        private readonly ConcurrentDictionary<string, CacheItem> _memory = new();
        private readonly ConcurrentDictionary<string, bool> _indexed = new();
        private readonly ConcurrentDictionary<string, ILiteCollection<CacheRecord>> _collections = new();
        private readonly CacheExpiryScheduler _expiryScheduler;
        private readonly Task _workerTask;
        private readonly bool disableLocalCache;
        private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard.WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance);
        private const int CacheSchemaVersion = 2;

        private class CacheItem
        {
            public object Value;
            public long ExpiryTicks;
        }

        private class CacheRecord
        {
            [BsonId]
            public string Key { get; set; }

            [BsonField]
            public byte[] Data { get; set; }

            public long ExpiryTicks { get; set; }
        }

        private class WriteItem
        {
            public string Type;
            public string Id;
            public byte[] Data;
            public long ExpiryTicks;
        }

        public LiteDbCacheStore(string path, bool disableLocalCache)
        {
            this.disableLocalCache = disableLocalCache;

            _expiryScheduler = new CacheExpiryScheduler(CleanupMemory);

            if (disableLocalCache) return;

            var versionFile = Path.Combine(path, "cache.version");

            bool rebuild = false;

            try
            {
                if (!File.Exists(versionFile))
                {
                    rebuild = true;
                }
                else
                {
                    var text = File.ReadAllText(versionFile);

                    if (!int.TryParse(text, out var version) ||
                        version != CacheSchemaVersion)
                    {
                        rebuild = true;
                    }
                }
            }
            catch
            {
                rebuild = true;
            }

            if (rebuild)
            {
                try
                {
                    Directory.Delete(path, recursive: true);
                }
                catch
                {
                }

                Directory.CreateDirectory(path);

                File.WriteAllText(versionFile, CacheSchemaVersion.ToString());
            }

            _db = new LiteDatabase($"Filename={path}\\cache.db;Connection=shared;");

            _workerTask = Task.Run(ProcessQueue);
        }

        private void CleanupMemory()
        {
            var now = DateTime.UtcNow.Ticks;

            foreach (var kv in _memory)
            {
                if (kv.Value.ExpiryTicks < now)
                    _memory.TryRemove(kv.Key, out _);
            }
        }

        private readonly ConcurrentQueue<WriteItem> _writeQueue = new();
        private readonly SemaphoreSlim _signal = new(0);
        private readonly CancellationTokenSource _cts = new();

        private async Task ProcessQueue()
        {
            if (disableLocalCache || _db == null) return;

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    await _signal.WaitAsync(_cts.Token).ConfigureAwait(false);

                    if (_cts.IsCancellationRequested)
                        break;

                    await Task.Delay(TimeSpan.FromSeconds(5), _cts.Token).ConfigureAwait(false);

                    var batchMap = new Dictionary<(string type, string id), WriteItem>();
                    while (_writeQueue.TryDequeue(out var item))
                    {
                        batchMap[(item.Type, item.Id)] = item;
                    }

                    var batch = batchMap.Values.ToList();

                    if (batch.Count == 0)
                        continue;

                    var grouped = batch.GroupBy(x => x.Type);

                    foreach (var group in grouped)
                    {
                        var col = _collections.GetOrAdd(group.Key, k => _db.GetCollection<CacheRecord>(k));

                        if (_indexed.TryAdd(group.Key, true))
                        {
                            col.EnsureIndex(x => x.ExpiryTicks);
                        }

                        var records = group.Select(x => new CacheRecord
                        {
                            Key = x.Id,
                            Data = x.Data,
                            ExpiryTicks = x.ExpiryTicks
                        });

                        col.Upsert(records);
                    }

                    if (DateTime.UtcNow - _lastFileCleanup > FileCleanupInterval)
                    {
                        var now = DateTime.UtcNow.Ticks;

                        foreach (var name in _db.GetCollectionNames())
                        {
                            var col = _db.GetCollection<CacheRecord>(name);
                            col.EnsureIndex(x => x.ExpiryTicks);
                            try
                            {
                                col.DeleteMany(x => x.ExpiryTicks < now);
                            }
                            catch { }
                        }

                        _lastFileCleanup = DateTime.UtcNow;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
            }
        }

        private static (string type, string id) ParseKey(string key)
        {
            var idx = key.IndexOf(':');
            if (idx < 0)
                return ("cache", key);

            return (
                key.Substring(0, idx),
                key.Substring(idx + 1)
            );
        }

        public T Get<T>(string key)
        {
            var now = DateTime.UtcNow.Ticks;

            if (_memory.TryGetValue(key, out var mem))
            {
                if (mem != default && mem.ExpiryTicks > now)
                    return (T)mem.Value;

                _memory.TryRemove(key, out _);
            }

            if (disableLocalCache || _db == null)
            {
                return default(T);
            }

            var (type, id) = ParseKey(key);
            var col = _collections.GetOrAdd(type, k => _db.GetCollection<CacheRecord>(k));
            var record = col.FindById(id);
            if (record == null)
            {
                return default;
            }

            if (record.ExpiryTicks < now || record.Data == null || record.Data.Length == 0)
            {
                col.Delete(id);
                return default;
            }
            try
            {
                var value = MessagePackSerializer.Deserialize<T>(record.Data, Options);
                _memory[key] = new CacheItem
                {
                    Value = value,
                    ExpiryTicks = record.ExpiryTicks
                };

                return value;
            }
            catch
            {
                col.Delete(id);
                return default;
            }
        }

        public void Put<T>(string key, T value, DateTime expiry)
        {
            var expiryTicks = expiry.ToUniversalTime().Ticks;

            _memory[key] = new CacheItem
            {
                Value = value,
                ExpiryTicks = expiryTicks
            };
            _expiryScheduler.Schedule(expiryTicks);

            if (disableLocalCache || _db == null)
            {
                return;
            }

            var (type, id) = ParseKey(key);

            _writeQueue.Enqueue(new WriteItem
            {
                Type = type,
                Id = id,
                Data = MessagePackSerializer.Serialize(value, Options),
                ExpiryTicks = expiryTicks
            });

            _signal.Release();
        }

        private void FlushRemaining()
        {
            if (disableLocalCache || _db == null) return;

            var batchMap = new Dictionary<(string type, string id), WriteItem>();
            while (_writeQueue.TryDequeue(out var item))
            {
                batchMap[(item.Type, item.Id)] = item;
            }

            var batch = batchMap.Values.ToList();

            if (batch.Count == 0)
                return;

            var grouped = batch.GroupBy(x => x.Type);

            foreach (var group in grouped)
            {
                var col = _collections.GetOrAdd(group.Key, k => _db.GetCollection<CacheRecord>(k));

                var records = group.Select(x => new CacheRecord
                {
                    Key = x.Id,
                    Data = x.Data,
                    ExpiryTicks = x.ExpiryTicks
                });

                col.Upsert(records);
            }

            var now = DateTime.UtcNow.Ticks;
            foreach (var col in _collections.Values)
            {
                try
                {
                    col.DeleteMany(x => x.ExpiryTicks < now);
                }
                catch { /* collection may not exist or be empty */ }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _signal.Release();
            try
            {
                _workerTask?.Wait();
            }
            catch { }
            FlushRemaining();

            _expiryScheduler.Dispose();
            _db?.Dispose();
        }
    }
}
