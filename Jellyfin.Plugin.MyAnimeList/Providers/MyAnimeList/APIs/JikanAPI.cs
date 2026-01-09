using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using JikanDotNet;
using MediaBrowser.Common.Configuration;
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
        private static bool _initialized;
        private static readonly object _initLock = new();
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
                RelationsCacheFile = Path.Combine(baseDir, "JikanRelationsCache.json");
                PicturesCacheFile = Path.Combine(baseDir, "JikanPicturesCache.json");

                LoadCache(AnimeCacheFile, _animeCache);
                LoadCache(SearchCacheFile, _searchCache);
                LoadCache(EpisodesCacheFile, _episodesCache);
                LoadCache(CharactersCacheFile, _charactersCache);
                LoadCache(RelationsCacheFile, _relationsCache);
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

        private static readonly TimeSpan DefaultExpiry = TimeSpan.FromDays(1);
        private static readonly TimeSpan SearchExpiry = TimeSpan.FromMinutes(10);

        private class CacheEntry
        {
            public DateTime Expiry { get; set; } = DateTime.UtcNow.Add(DefaultExpiry);

            public AnimeCacheDto Anime { get; set; }
            public List<long> Ids { get; set; }
            public List<CharacterCacheDto> Characters { get; set; }
            public Dictionary<int, EpisodeCacheDto> Episodes { get; set; }
            public List<RelatedEntryDto> Relations { get; set; }
            public List<ImagesSetDto> Pictures { get; set; }
        }

        private static readonly ConcurrentDictionary<string, CacheEntry> _animeCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> _searchCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> _episodesCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> _charactersCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> _relationsCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> _picturesCache = new();
        private static string AnimeCacheFile;
        private static string SearchCacheFile;
        private static string EpisodesCacheFile;
        private static string CharactersCacheFile;
        private static string RelationsCacheFile;
        private static string PicturesCacheFile;

        private static void LoadCache(string filePath, ConcurrentDictionary<string, CacheEntry> cache)
        {
            if (Plugin.Instance.Configuration.DisableLocalCache) return;
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
            if (Plugin.Instance.Configuration.DisableLocalCache) return;

            foreach (var kvp in cache.Where(kvp => kvp.Value.Expiry <= DateTime.UtcNow).ToList())
            {
                cache.TryRemove(kvp.Key, out _);
            }

            var json = JsonSerializer.Serialize(cache, JsonOptions);
            var tempFile = filePath + ".tmp";
            await File.WriteAllTextAsync(tempFile, json).ConfigureAwait(false);
            File.Replace(tempFile, filePath, null);
        }

        private static async Task<T> GetOrFetchAsync<T>(
            string key,
            ConcurrentDictionary<string, CacheEntry> cache,
            string cacheFile,
            Func<Task<T>> fetchFunc,
            Func<CacheEntry, T> getter,
            Action<CacheEntry, T> setter,
            bool persist)
            where T : class
        {
            if (cache.TryGetValue(key, out var entry) && entry.Expiry > DateTime.UtcNow)
            {
                var value = getter(entry);
                if (value != null)
                    return value;
            }

            var result = await fetchFunc();

            var newEntry = entry ?? new CacheEntry();
            newEntry.Expiry = DateTime.UtcNow.Add(DefaultExpiry);

            setter(newEntry, result);

            cache[key] = newEntry;

            if (persist)
                await SaveCacheAsync(cacheFile, cache);

            return result;
        }


        public static Task<AnimeCacheDto> GetAnimeAsync(long malId, CancellationToken token) =>
            GetOrFetchAsync(
                malId.ToString(),
                _animeCache,
                AnimeCacheFile,
                async () => AnimeCacheDto.From((await Instance.GetAnimeAsync(malId, token)).Data),
                entry => entry.Anime,
                (entry, value) => entry.Anime = value,
                true
            );

        public static Task<EpisodeCacheDto> GetAnimeEpisodeAsync(long malId, int episodeNumber, CancellationToken token) =>
            GetOrFetchAsync(
                malId.ToString(),
                _episodesCache,
                EpisodesCacheFile,
                async () =>
                {
                    var result = await Instance.GetAnimeEpisodeAsync(malId, episodeNumber, token);
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
                },
                true
            );

        public static Task<List<CharacterCacheDto>> GetAnimeCharactersAsync(long malId, CancellationToken token) =>
            GetOrFetchAsync(
                malId.ToString(),
                _charactersCache,
                CharactersCacheFile,
                async () =>
                {
                    var result = await Instance.GetAnimeCharactersAsync(malId, token);
                    return result.Data.Select(CharacterCacheDto.From).ToList();
                },
                entry => entry.Characters,
                (entry, value) => entry.Characters = value,
                true
            );

        public static Task<List<RelatedEntryDto>> GetAnimeRelationsAsync(long malId, CancellationToken token) =>
            GetOrFetchAsync(
                malId.ToString(),
                _relationsCache,
                RelationsCacheFile,
                async () =>
                {
                    var result = await Instance.GetAnimeRelationsAsync(malId, token);
                    return result.Data.Select(RelatedEntryDto.From).Where(r => r != null).ToList();
                },
                entry => entry.Relations,
                (entry, value) => entry.Relations = value,
                true
            );

        public static Task<List<ImagesSetDto>> GetAnimePicturesAsync(long malId, CancellationToken token) =>
            GetOrFetchAsync(
                malId.ToString(),
                _picturesCache,
                PicturesCacheFile,
                async () =>
                {
                    var result = await Instance.GetAnimePicturesAsync(malId, token);
                    return result.Data.Select(ImagesSetDto.From).Where(r => r != null).ToList();
                },
                entry => entry.Pictures,
                (entry, value) => entry.Pictures = value,
                true
            );

        public static async Task<List<AnimeCacheDto>> SearchAnimeAsync(string term, CancellationToken token)
        {
            if (_searchCache.TryGetValue(term, out var cached) &&
                cached.Expiry > DateTime.UtcNow &&
                cached.Ids != null)
            {
                return (await Task.WhenAll(cached.Ids.Select(id => GetAnimeAsync(id, token)))).ToList();
            }

            var result = await Instance.SearchAnimeAsync(term, token);
            var ids = result.Data.Select(a => a.MalId ?? 0).ToList();

            var anime = await Task.WhenAll(
                result.Data.Select(a =>
                    GetOrFetchAsync(
                        a.MalId.ToString(),
                        _animeCache,
                        AnimeCacheFile,
                        () => Task.FromResult(AnimeCacheDto.From(a)),
                        entry => entry.Anime,
                        (entry, value) => entry.Anime = value,
                        false
                    )
                )
            );
            await SaveCacheAsync(AnimeCacheFile, _animeCache);

            _searchCache[term] = new CacheEntry
            {
                Ids = ids,
                Expiry = DateTime.UtcNow.Add(SearchExpiry)
            };

            await SaveCacheAsync(SearchCacheFile, _searchCache);
            return anime.ToList();
        }
    }
}
