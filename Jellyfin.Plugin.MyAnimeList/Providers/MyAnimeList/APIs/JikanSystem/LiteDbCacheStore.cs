using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LiteDB;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem
{
    public class LiteDbCacheStore : IDisposable, ICacheStore
    {
        private readonly LiteDatabase _db;

        private readonly ConcurrentDictionary<string, CacheItem> _memory = new();
        private readonly ConcurrentDictionary<string, bool> _indexed = new();
        private readonly Timer _cleanupTimer;
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
           
            _cleanupTimer = new Timer(_ =>
            {
                var now = DateTime.UtcNow.Ticks;

                foreach (var kv in _memory)
                {
                    if (kv.Value.ExpiryTicks < now)
                        _memory.TryRemove(kv.Key, out CacheItem _);
                }
            }, null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

            if (disableLocalCache) return;

            _db = new LiteDatabase($"{path}\\cache.db");
            Task.Run(ProcessQueue);
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

                    while (_writeQueue.TryDequeue(out var item))
                    {
                        try
                        {
                            var json = System.Text.Json.JsonSerializer.Serialize(item.Value);

                            var (type, id) = ParseKey(item.Key);
                            var col = _db.GetCollection<CacheRecord>(type);

                            col.Upsert(new CacheRecord
                            {
                                Key = id,
                                DataJson = json,
                                ExpiryTicks = item.ExpiryTicks
                            });
                        }
                        catch
                        {

                        }
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
            var col = _db.GetCollection<CacheRecord>(type);
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

            T value;

            if (record.ExpiryTicks < now || string.IsNullOrEmpty(record.DataJson))
            {
                col.Delete(id);
                value = default;
            }
            else
            {
                try
                {
                    value = System.Text.Json.JsonSerializer.Deserialize<T>(record.DataJson);

                    if (value == null)
                    {
                        col.Delete(id);
                        return default;
                    }
                }
                catch
                {
                    col.Delete(id);
                    return default;
                }
            }

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

        public void Dispose()
        {
            _cts.Cancel();
            _signal.Release();
            _cleanupTimer?.Dispose();
            _db?.Dispose();
        }
    }
}
