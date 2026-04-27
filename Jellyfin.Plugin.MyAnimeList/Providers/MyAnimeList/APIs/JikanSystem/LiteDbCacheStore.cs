using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem
{
    public class LiteDbCacheStore : IDisposable, ICacheStore
    {
        private readonly LiteDatabase _db;
        private static LiteDatabase _sharedDb;
        private static readonly object _dbLock = new();

        private readonly ConcurrentDictionary<string, CacheItem> _memory = new();
        private readonly ConcurrentDictionary<string, bool> _indexed = new();
        private readonly ConcurrentDictionary<string, ILiteCollection<CacheRecord>> _collections = new();
        private readonly CacheExpiryScheduler _expiryScheduler;
        private readonly Task _workerTask;
        private readonly bool disableLocalCache;

        private class CacheItem
        {
            public object Value;
            public long ExpiryTicks;
        }

        private class CacheRecord
        {
            [BsonId]
            public string Key { get; set; }

            public string DataJson { get; set; }

            public long ExpiryTicks { get; set; }
        }

        private class WriteItem
        {
            public string Key;
            public object Value;
            public long ExpiryTicks;
        }

        public LiteDbCacheStore(string path, bool disableLocalCache)
        {
            this.disableLocalCache = disableLocalCache;

            _expiryScheduler = new CacheExpiryScheduler(CleanupMemory);

            if (disableLocalCache) return;

            lock (_dbLock)
            {
                if (_sharedDb == null)
                {
                    _sharedDb = new LiteDatabase($"Filename={path}\\cache.db;Connection=shared;");
                }

                _db = _sharedDb;
            }

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

                    var batchMap = new Dictionary<string, WriteItem>();

                    while (_writeQueue.TryDequeue(out var item))
                    {
                        batchMap[item.Key] = item;
                    }

                    var batch = batchMap.Values.ToList();

                    if (batch.Count == 0)
                        continue;

                    var grouped = batch
                        .Select(item =>
                        {
                            var (type, id) = ParseKey(item.Key);
                            return (item, type, id);
                        })
                        .GroupBy(x => x.type);

                    foreach (var group in grouped)
                    {
                        var col = _collections.GetOrAdd(group.Key, k => _db.GetCollection<CacheRecord>(k));

                        if (_indexed.TryAdd(group.Key, true))
                        {
                            col.EnsureIndex(x => x.Key);
                            col.EnsureIndex(x => x.ExpiryTicks);
                        }

                        var records = group.Select(x => new CacheRecord
                        {
                            Key = x.id,
                            DataJson = System.Text.Json.JsonSerializer.Serialize(x.item.Value),
                            ExpiryTicks = x.item.ExpiryTicks
                        });

                        col.Upsert(records);
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
            if (_indexed.TryAdd(type, true))
            {
                col.EnsureIndex(x => x.Key);
                col.EnsureIndex(x => x.ExpiryTicks);
            }
            var record = col.FindById(id);
            if (record == null)
            {
                return default;
            }

            if (record.ExpiryTicks < now || string.IsNullOrEmpty(record.DataJson))
            {
                col.Delete(id);
                return default;
            }

            T value = System.Text.Json.JsonSerializer.Deserialize<T>(record.DataJson);
            _memory[key] = new CacheItem
            {
                Value = value,
                ExpiryTicks = record.ExpiryTicks
            };

            return value;
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

            _writeQueue.Enqueue(new WriteItem
            {
                Key = key,
                Value = value,
                ExpiryTicks = expiryTicks
            });

            _signal.Release();
        }

        private void FlushRemaining()
        {
            if (disableLocalCache || _db == null) return;

            var batchMap = new Dictionary<string, WriteItem>();
            while (_writeQueue.TryDequeue(out var item))
            {
                batchMap[item.Key] = item;
            }

            var batch = batchMap.Values.ToList();

            if (batch.Count == 0)
                return;

            var grouped = batch
                .Select(item =>
                {
                    var (type, id) = ParseKey(item.Key);
                    return (item, type, id);
                })
                .GroupBy(x => x.type);

            foreach (var group in grouped)
            {
                var col = _collections.GetOrAdd(group.Key, k => _db.GetCollection<CacheRecord>(k));

                var records = group.Select(x => new CacheRecord
                {
                    Key = x.id,
                    DataJson = System.Text.Json.JsonSerializer.Serialize(x.item.Value),
                    ExpiryTicks = x.item.ExpiryTicks
                });

                col.Upsert(records);
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
