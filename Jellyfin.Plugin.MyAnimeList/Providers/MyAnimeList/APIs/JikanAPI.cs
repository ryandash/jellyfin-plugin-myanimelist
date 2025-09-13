using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using JikanDotNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs
{
    public static class JikanSingleton
    {
        static JikanSingleton()
        {
            LoadCacheFromFile();
        }

        private static readonly Lazy<Jikan> _jikanInstance = new Lazy<Jikan>(() => new Jikan());
        private static Jikan Instance => _jikanInstance.Value;

        private static readonly string CacheFilePath = Path.Combine(Plugin.Instance.DataFolderPath, "JikanCache.json");

        // Store cache as serialized JSON strings
        private static readonly ConcurrentDictionary<string, CacheEntry> _cache
            = new ConcurrentDictionary<string, CacheEntry>();

        private class CacheEntry
        {
            public DateTime Expiry { get; set; }
            public JsonElement JsonValue { get; set; }
            public string Type { get; set; }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static string GetKey(string method, object param) => $"{method}:{param}";

        private static async Task SaveCacheToFileAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_cache, JsonOptions);

                var tempFilePath = Path.Combine(Plugin.Instance.DataFolderPath, "JikanCache.tmp");
                await File.WriteAllTextAsync(tempFilePath, json).ConfigureAwait(false);

                File.Replace(tempFilePath, CacheFilePath, null);
            }
            catch (Exception ex)
            {
                var debugPath = Path.Combine(Plugin.Instance.DataFolderPath, "JikanCache.debug.log");
                File.AppendAllText(debugPath,
                    $"[{DateTime.UtcNow}] ERROR saving cache: {ex}{Environment.NewLine}");
            }
        }

        private static void LoadCacheFromFile()
        {
            try
            {
                if (!File.Exists(CacheFilePath))
                {
                    File.WriteAllText(CacheFilePath, "{}");
                    return;
                }

                var json = File.ReadAllText(CacheFilePath);

                var debugPath = Path.Combine(Plugin.Instance.DataFolderPath, "JikanCache.debug.log");
                File.AppendAllText(debugPath,
                    $"[{DateTime.UtcNow}] Starting load. JSON length: {json.Length}{Environment.NewLine}");

                var loaded = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json, JsonOptions);

                if (loaded == null)
                {
                    File.AppendAllText(debugPath,
                        $"[{DateTime.UtcNow}] Deserialization returned null.{Environment.NewLine}");
                    return;
                }

                int kept = 0;
                foreach (var kvp in loaded)
                {
                    if (kvp.Value.Expiry > DateTime.UtcNow)
                    {
                        _cache[kvp.Key] = kvp.Value;
                        kept++;
                    }
                    else
                    {
                        File.AppendAllText(debugPath,
                            $"[{DateTime.UtcNow}] Dropped expired entry {kvp.Key}, Expiry={kvp.Value.Expiry}{Environment.NewLine}");
                    }
                }

                File.AppendAllText(debugPath,
                    $"[{DateTime.UtcNow}] Loaded {kept}/{loaded.Count} entries into cache.{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                File.AppendAllText(
                    Path.Combine(Plugin.Instance.DataFolderPath, "JikanCache.debug.log"),
                    $"[{DateTime.UtcNow}] ERROR loading cache: {ex}{Environment.NewLine}"
                );
            }
        }

        private static async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.Expiry > DateTime.UtcNow)
                {
                    try
                    {
                        return JsonSerializer.Deserialize<T>(entry.JsonValue, JsonOptions);
                    }
                    catch
                    {
                        // fall through and refresh
                    }
                }
                _cache.TryRemove(key, out _);
            }

            var result = await factory().ConfigureAwait(false);

            _cache[key] = new CacheEntry
            {
                Expiry = DateTime.UtcNow.AddDays(1),
                JsonValue = JsonSerializer.SerializeToElement(result, JsonOptions),
                Type = typeof(T).FullName
            };

            await SaveCacheToFileAsync();

            return result;
        }

        // Cached wrappers
        public static Task<AnimeCacheDto> GetAnimeAsync(long malId, CancellationToken token)
        {
            return GetOrAddAsync(
                GetKey("GetAnime", malId),
                async () =>
                {
                    var result = await Instance.GetAnimeAsync(malId, token).ConfigureAwait(false);
                    return AnimeCacheDto.From(result.Data);
                });
        }

        public static Task<EpisodeCacheDto> GetAnimeEpisodeAsync(long malId, int episodeNumber, CancellationToken token)
        {
            return GetOrAddAsync(
                GetKey("GetAnimeEpisode", $"{malId}:{episodeNumber}"),
                async () =>
                {
                    var result = await Instance.GetAnimeEpisodeAsync(malId, episodeNumber, token).ConfigureAwait(false);
                    return EpisodeCacheDto.From(result.Data);
                });
        }

        public static Task<List<CharacterCacheDto>> GetAnimeCharactersAsync(long malId, CancellationToken token)
        {
            return GetOrAddAsync(
                GetKey("GetAnimeCharacters", malId),
                async () =>
                {
                    var result = await Instance.GetAnimeCharactersAsync(malId, token).ConfigureAwait(false);
                    return result.Data.Select(CharacterCacheDto.From).ToList();
                });
        }

        // Keep full API responses for relations, pictures, and search if you still need them
        public static Task<PaginatedJikanResponse<ICollection<RelatedEntry>>> GetAnimeRelationsAsync(long malId, CancellationToken token) =>
            GetOrAddAsync(GetKey("GetAnimeRelations", malId), () => Instance.GetAnimeRelationsAsync(malId, token));

        public static Task<BaseJikanResponse<ICollection<ImagesSet>>> GetAnimePicturesAsync(long malId, CancellationToken token) =>
            GetOrAddAsync(GetKey("GetAnimePictures", malId), () => Instance.GetAnimePicturesAsync(malId, token));

        public static Task<List<AnimeCacheDto>> SearchAnimeAsync(string searchTerm, CancellationToken token)
        {
            return GetOrAddAsync(
                GetKey("SearchAnime", searchTerm),
                async () =>
                {
                    var result = await Instance.SearchAnimeAsync(searchTerm, token).ConfigureAwait(false);
                    return result.Data.Select(anime => AnimeCacheDto.From(anime)).ToList();
                });
        }
    }
}
