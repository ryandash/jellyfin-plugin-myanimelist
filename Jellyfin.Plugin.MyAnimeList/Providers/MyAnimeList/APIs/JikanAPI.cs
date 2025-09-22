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
            LoadCache(AnimeCacheFile, _animeCache);
            LoadCache(SearchCacheFile, _searchCache);
            LoadCache(EpisodesCacheFile, _episodesCache);
            LoadCache(CharactersCacheFile, _charactersCache);
            LoadCache(RelationsCacheFile, _relationsCache);
            LoadCache(PicturesCacheFile, _picturesCache);
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

        private static readonly string AnimeCacheFile = Path.Combine(Plugin.Instance.DataFolderPath, "JikanAnimeCache.json");
        private static readonly string SearchCacheFile = Path.Combine(Plugin.Instance.DataFolderPath, "JikanSearchCache.json");
        private static readonly string EpisodesCacheFile = Path.Combine(Plugin.Instance.DataFolderPath, "JikanEpisodesCache.json");
        private static readonly string CharactersCacheFile = Path.Combine(Plugin.Instance.DataFolderPath, "JikanCharactersCache.json");
        private static readonly string RelationsCacheFile = Path.Combine(Plugin.Instance.DataFolderPath, "JikanRelationsCache.json");
        private static readonly string PicturesCacheFile = Path.Combine(Plugin.Instance.DataFolderPath, "JikanPicturesCache.json");

        private static void LoadCache(string filePath, ConcurrentDictionary<string, CacheEntry> cache)
        {
            if (Plugin.Instance.Configuration.DisableLocalCache) return;
            try
            {
                if (!File.Exists(filePath)) { File.WriteAllText(filePath, "{}"); return; }
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
            Action<CacheEntry, T> setter)
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
                (entry, value) => entry.Anime = value);

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
                    if (entry.Episodes == null) entry.Episodes = new Dictionary<int, EpisodeCacheDto>();
                    entry.Episodes[episodeNumber] = value;
                });

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
                (entry, value) => entry.Characters = value);

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
                (entry, value) => entry.Relations = value);

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
                (entry, value) => entry.Pictures = value);

        public static async Task<List<AnimeCacheDto>> SearchAnimeAsync(string searchTerm, CancellationToken token)
        {
            if (_searchCache.TryGetValue(searchTerm, out var cached) &&
                cached.Expiry > DateTime.UtcNow &&
                cached.Ids != null)
            {
                var list = new List<AnimeCacheDto>();
                foreach (var id in cached.Ids)
                    list.Add(await GetAnimeAsync(id, token));
                return list;
            }

            var result = await Instance.SearchAnimeAsync(searchTerm, token);
            var malIds = result.Data.Select(a => a.MalId ?? 0).ToList();

            var animeList = new List<AnimeCacheDto>();
            foreach (var id in malIds)
                animeList.Add(await GetAnimeAsync(id, token));

            _searchCache[searchTerm] = new CacheEntry
            {
                Ids = malIds,
                Expiry = DateTime.UtcNow.Add(SearchExpiry)
            };
            await SaveCacheAsync(SearchCacheFile, _searchCache);

            return animeList;
        }
    }
}
