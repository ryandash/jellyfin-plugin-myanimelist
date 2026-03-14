using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using JikanDotNet;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs
{
    public static class JikanAPI
    {
        private static bool _initialized;
        private static readonly object _initLock = new();
        private static readonly PluginConfiguration config = Plugin.Instance.Configuration;
        public static void Initialize(IApplicationPaths paths)
        {
            if (_initialized) return;
            lock (_initLock)
            {
                _initialized = true;

                var baseDir = Path.Combine(paths.CachePath, "myanimelist");

                Directory.CreateDirectory(baseDir);

                AnimeCacheFile = Path.Combine(baseDir, "JikanAnimeCache.json");
                SearchCacheFile = Path.Combine(baseDir, "JikanSearchCache.json");
                EpisodesCacheFile = Path.Combine(baseDir, "JikanEpisodesCache.json");
                CharactersCacheFile = Path.Combine(baseDir, "JikanCharactersCache.json");
                PicturesCacheFile = Path.Combine(baseDir, "JikanPicturesCache.json");

                LoadCache(AnimeCacheFile, _animeCache);
                LoadCache(SearchCacheFile, _searchCache);
                LoadCache(EpisodesCacheFile, _episodesCache);
                LoadCache(CharactersCacheFile, _charactersCache);
                LoadCache(PicturesCacheFile, _picturesCache);
            }
        }

        private static readonly Lazy<Jikan> _jikanInstance = new(() => new Jikan());
        private static Jikan Instance => _jikanInstance.Value;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        private static TimeSpan OtherExpiry =>
            TimeSpan.FromDays(Plugin.Instance.Configuration.cacheOtherTime);

        private static TimeSpan SearchExpiry =>
            TimeSpan.FromMinutes(Plugin.Instance.Configuration.cacheSearchTime);
        private class CacheEntry
        {
            public DateTime Expiry { get; set; }

            public AnimeFullCacheDto Anime { get; set; }
            public List<long> Ids { get; set; }
            public List<CharacterCacheDto> Characters { get; set; }
            public Dictionary<int, EpisodeCacheDto> Episodes { get; set; }
            public List<ImagesSetDto> Pictures { get; set; }
        }

        private static readonly ConcurrentDictionary<string, CacheEntry> _animeCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> _searchCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> _episodesCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> _charactersCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> _picturesCache = new();
        private static string AnimeCacheFile;
        private static string SearchCacheFile;
        private static string EpisodesCacheFile;
        private static string CharactersCacheFile;
        private static string PicturesCacheFile;

        private static void LoadCache(string filePath, ConcurrentDictionary<string, CacheEntry> cache)
        {
            if (config.DisableLocalCache) return;
            try
            {
                var dir = Path.GetDirectoryName(filePath)!;
                Directory.CreateDirectory(dir);

                if (!File.Exists(filePath))
                {
                    File.WriteAllText(filePath, "{}");
                    return;
                }

                var json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json, JsonOptions);
                if (loaded == null) return;
                foreach (var kvp in loaded) cache[kvp.Key] = kvp.Value;
            }
            catch
            {
                File.WriteAllText(filePath, "{}");
                cache.Clear();
            }
        }

        private static async Task SaveCacheAsync(string filePath, ConcurrentDictionary<string, CacheEntry> cache)
        {
            if (config.DisableLocalCache) return;

            foreach (var kvp in cache.Where(kvp => kvp.Value.Expiry <= DateTime.UtcNow).ToList())
            {
                cache.TryRemove(kvp.Key, out _);
            }

            var json = JsonSerializer.Serialize(cache, JsonOptions);
            var tempFile = filePath + ".tmp";
            await File.WriteAllTextAsync(tempFile, json);
            File.Replace(tempFile, filePath, null);
        }

        private static async Task<T> GetOrFetchAsync<T>(
            string key,
            ConcurrentDictionary<string, CacheEntry> cache,
            string cacheFile,
            Func<Task<T>> fetchFunc,
            Func<CacheEntry, T> getter,
            Action<CacheEntry, T> setter,
            TimeSpan expiry,
            bool persist)
            where T : class
        {
            if (cache.TryGetValue(key, out var entry) && entry.Expiry > DateTime.UtcNow)
            {
                var value = getter(entry);
                if (value != null)
                    return value;
            }

            var result = await fetchFunc().ConfigureAwait(false);

            var newEntry = entry ?? new CacheEntry();
            newEntry.Expiry = DateTime.UtcNow.Add(expiry);

            setter(newEntry, result);

            cache[key] = newEntry;

            if (persist)
                await SaveCacheAsync(cacheFile, cache).ConfigureAwait(false);

            return result;
        }

        public static async Task<AnimeFullCacheDto> GetAnimeFullAsync(long malId, CancellationToken token, bool needRelations = false)
        {
            var cacheKey = malId.ToString();
            AnimeFullCacheDto animeCacheDto = null;

            if (_animeCache.TryGetValue(cacheKey, out var cachedEntry) && cachedEntry.Expiry > DateTime.UtcNow)
            {
                if (cachedEntry.Anime?.Relations != null || !needRelations)
                {
                    return cachedEntry.Anime;
                }

                var relationsData = await Instance.GetAnimeRelationsAsync(malId, token).ConfigureAwait(false);
                cachedEntry.Anime.Relations = RelatedEntryDto.FilterRelations(relationsData.Data);
                animeCacheDto = cachedEntry.Anime;
            }
            else
            {
                var fullAnimeData = await Instance.GetAnimeFullDataAsync(malId, token).ConfigureAwait(false);
                animeCacheDto = AnimeFullCacheDto.From(fullAnimeData.Data);
            }

            var finishedDate = animeCacheDto?.Aired?.To;
            var expiry = (finishedDate.HasValue && finishedDate.Value < DateTime.UtcNow.AddMonths(-1))
                ? OtherExpiry
                : SearchExpiry;

            _animeCache[cacheKey] = new CacheEntry
            {
                Anime = animeCacheDto,
                Expiry = DateTime.UtcNow.Add(expiry)
            };

            await SaveCacheAsync(AnimeCacheFile, _animeCache).ConfigureAwait(false);
            return animeCacheDto;
        }

        public static Task<EpisodeCacheDto> GetAnimeEpisodeAsync(long malId, int episodeNumber, CancellationToken token) =>
            GetOrFetchAsync(
                malId.ToString(),
                _episodesCache,
                EpisodesCacheFile,
                async () =>
                {
                    var result = await Instance.GetAnimeEpisodeAsync(malId, episodeNumber, token).ConfigureAwait(false);
                    return EpisodeCacheDto.From(result.Data);
                },
                entry =>
                {
                    if (entry.Episodes != null && entry.Episodes.TryGetValue(episodeNumber, out var existing))
                        return existing;
                    return null;
                },
                (entry, value) =>
                {
                    entry.Episodes ??= new Dictionary<int, EpisodeCacheDto>();
                    entry.Episodes[episodeNumber] = value;

                    var airedDate = value?.Aired;
                    var expiry = (airedDate.HasValue && airedDate.Value < DateTime.UtcNow.AddMonths(-1))
                        ? OtherExpiry
                        : SearchExpiry;

                    entry.Expiry = DateTime.UtcNow.Add(expiry);
                },
                SearchExpiry,
                true
            );

        public static async Task<List<AnimeFullCacheDto>> SearchAnimeAsync(string term, CancellationToken token)
        {
            if (_searchCache.TryGetValue(term, out var cached) &&
                cached.Expiry > DateTime.UtcNow &&
                cached.Ids != null)
            {
                return (await Task.WhenAll(cached.Ids.Select(id => GetAnimeFullAsync(id, token))).ConfigureAwait(false))
                .OrderBy(a => a.MalId)
                .ToList();
            }
            var result = await Instance.SearchAnimeAsync(term, token).ConfigureAwait(false);
            var sortedData = result.Data
                .Where(a => a.MalId.HasValue)
                .OrderBy(a => a.MalId.Value)
                .ToList();

            var anime = await Task.WhenAll(
                sortedData.Select(a =>
                    GetOrFetchAsync(
                        a.MalId.ToString(),
                        _animeCache,
                        AnimeCacheFile,
                        () => Task.FromResult(AnimeFullCacheDto.From(a)),
                        entry => entry.Anime,
                        (entry, value) => entry.Anime = value,
                        SearchExpiry,
                        false
                    )
                )
            ).ConfigureAwait(false);
            await SaveCacheAsync(AnimeCacheFile, _animeCache).ConfigureAwait(false);

            _searchCache[term] = new CacheEntry
            {
                Ids = sortedData.Select(a => a.MalId ?? 0).ToList(),
                Expiry = DateTime.UtcNow.Add(SearchExpiry)
            };

            await SaveCacheAsync(SearchCacheFile, _searchCache).ConfigureAwait(false);
            return anime.ToList();
        }

        public static Task<List<CharacterCacheDto>> GetAnimeCharactersAsync(long malId, CancellationToken token) =>
            GetOrFetchAsync(
                malId.ToString(),
                _charactersCache,
                CharactersCacheFile,
                async () =>
                {
                    var result = await Instance.GetAnimeCharactersAsync(malId, token).ConfigureAwait(false);
                    return result.Data.Select(CharacterCacheDto.From).ToList();
                },
                entry => entry.Characters,
                (entry, value) => entry.Characters = value,
                OtherExpiry,
                true
            );

        public static Task<List<ImagesSetDto>> GetAnimePicturesAsync(long malId, CancellationToken token) =>
            GetOrFetchAsync(
                malId.ToString(),
                _picturesCache,
                PicturesCacheFile,
                async () =>
                {
                    var result = await Instance.GetAnimePicturesAsync(malId, token).ConfigureAwait(false);
                    return result.Data.Select(ImagesSetDto.From).Where(r => r != null).ToList();
                },
                entry => entry.Pictures,
                (entry, value) => entry.Pictures = value,
                OtherExpiry,
                true
            );
    }
}
