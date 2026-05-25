using LiteDB;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JsonSerializer = System.Text.Json.JsonSerializer;

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
        private const int CacheSchemaVersion = 6;

        private class CacheItem
        {
            public object Value;
            public long ExpiryTicks;
        }

        private class CacheRecord
        {
            [BsonId]
            public string Key { get; set; }

            public string Data { get; set; }

            public long ExpiryTicks { get; set; }
        }

        private class WriteItem
        {
            public string Type;
            public string Id;
            public object Value;
            public long ExpiryTicks;
        }

        public LiteDbCacheStore(string path, bool disableLocalCache)
        {
            this.disableLocalCache = disableLocalCache;

            _expiryScheduler = new CacheExpiryScheduler(CleanupMemory);

            if (disableLocalCache) return;

            foreach (var file in Directory.GetFiles(path))
            {
                if (!file.EndsWith($"cache_v{CacheSchemaVersion}.db"))
                {
                    try { File.Delete(file); } catch { }
                }
            }

            _db = new LiteDatabase($"Filename={path}\\cache_v{CacheSchemaVersion}.db;Connection=shared;");

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
            if (disableLocalCache || _db is null) return;

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
                            Data = JsonSerializer.Serialize(x.Value),
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

            if (disableLocalCache || _db is null)
            {
                return default;
            }

            var (type, id) = ParseKey(key);
            var col = _collections.GetOrAdd(type, k => _db.GetCollection<CacheRecord>(k));

            try
            {
                var record = col.FindById(id);

                if (record is null ||
                    record.ExpiryTicks < now ||
                    record.Data is null)
                {
                    col.Delete(id);
                    return default;
                }

                var value = JsonSerializer.Deserialize<T>(record.Data);

                if (value is null)
                {
                    col.Delete(id);
                    return default;
                }

                _memory[key] = new CacheItem
                {
                    Value = value,
                    ExpiryTicks = record.ExpiryTicks
                };

                return value;
            }
            catch
            {
                try
                {
                    col.Delete(id);
                }
                catch
                {
                }

                return default;
            }
        }

        public void Put<T>(string key, T value, DateTime expiry)
        {
            if (value is null) return;
            var expiryTicks = expiry.ToUniversalTime().Ticks;

            _memory[key] = new CacheItem
            {
                Value = value,
                ExpiryTicks = expiryTicks
            };
            _expiryScheduler.Schedule(expiryTicks);

            if (disableLocalCache || _db is null)
            {
                return;
            }

            var (type, id) = ParseKey(key);

            _writeQueue.Enqueue(new WriteItem
            {
                Type = type,
                Id = id,
                Value = value,
                ExpiryTicks = expiryTicks
            });

            _signal.Release();
        }

        private void FlushRemaining()
        {
            if (disableLocalCache || _db is null) return;

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
                    Data = JsonSerializer.Serialize(x.Value),
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
                _workerTask?.GetAwaiter().GetResult();
            }
            catch { }
            FlushRemaining();

            _expiryScheduler.Dispose();
            _db?.Dispose();
        }
    }
}
