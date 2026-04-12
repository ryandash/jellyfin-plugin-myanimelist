using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using JikanDotNet;
using JikanDotNet.Config;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs
{
    public static class JikanAPI
    {
        private static readonly object InitLock = new();
        private static bool _initialized;

        private static PluginConfiguration Config => Plugin.Instance.Configuration;

        private static readonly HttpClient HttpClient =
            new(new JikanHeaderHandler(new HttpClientHandler()))
            {
                BaseAddress = new Uri("https://api.jikan.moe/v4/")
            };

        private static readonly Lazy<Jikan> JikanInstance =
            new(() => new Jikan(new JikanClientConfiguration(), HttpClient));

        private static Jikan Instance => JikanInstance.Value;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true
        };

        private static readonly ConcurrentDictionary<string, CacheEntry> AnimeCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> SearchCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> EpisodesCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> CharactersCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> PicturesCache = new();

        private static string AnimeCacheFile;
        private static string SearchCacheFile;
        private static string EpisodesCacheFile;
        private static string CharactersCacheFile;
        private static string PicturesCacheFile;

        private static TimeSpan BackupExpiry => TimeSpan.FromDays(Config.cacheBackupOtherTime);
        private static TimeSpan SearchExpiry => TimeSpan.FromMinutes(Config.cacheSearchTime);

        private class CacheEntry
        {
            public DateTime Expiry { get; set; }
            public AnimeFullCacheDto Anime { get; set; }
            public List<long> Ids { get; set; }
            public List<CharacterCacheDto> Characters { get; set; }
            public Dictionary<int, EpisodeCacheDto> Episodes { get; set; }
            public List<ImagesSetDto> Pictures { get; set; }
        }

        public static void Initialize(IApplicationPaths paths)
        {
            if (_initialized) return;

            lock (InitLock)
            {
                if (_initialized) return;
                _initialized = true;

                var baseDir = Path.Combine(paths.CachePath, "myanimelist");
                Directory.CreateDirectory(baseDir);

                AnimeCacheFile = Path.Combine(baseDir, "JikanAnimeCache.json");
                SearchCacheFile = Path.Combine(baseDir, "JikanSearchCache.json");
                EpisodesCacheFile = Path.Combine(baseDir, "JikanEpisodesCache.json");
                CharactersCacheFile = Path.Combine(baseDir, "JikanCharactersCache.json");
                PicturesCacheFile = Path.Combine(baseDir, "JikanPicturesCache.json");

                LoadCache(AnimeCacheFile, AnimeCache);
                LoadCache(SearchCacheFile, SearchCache);
                LoadCache(EpisodesCacheFile, EpisodesCache);
                LoadCache(CharactersCacheFile, CharactersCache);
                LoadCache(PicturesCacheFile, PicturesCache);
            }
        }

        private static void LoadCache(string path, ConcurrentDictionary<string, CacheEntry> cache)
        {
            if (Config.DisableLocalCache) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                if (!File.Exists(path))
                {
                    File.WriteAllText(path, "{}");
                    return;
                }

                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json, JsonOptions);

                if (data == null) return;

                foreach (var kv in data)
                    cache[kv.Key] = kv.Value;
            }
            catch
            {
                cache.Clear();
                File.WriteAllText(path, "{}");
            }
        }

        private static async Task SaveCacheAsync(string path, ConcurrentDictionary<string, CacheEntry> cache)
        {
            if (Config.DisableLocalCache) return;

            var now = DateTime.UtcNow;

            foreach (var key in cache.Where(k => k.Value.Expiry <= now).Select(k => k.Key).ToList())
                cache.TryRemove(key, out _);

            var temp = path + ".tmp";
            var json = JsonSerializer.Serialize(cache, JsonOptions);

            await File.WriteAllTextAsync(temp, json);
            File.Replace(temp, path, null);
        }

        private static async Task<T> GetOrFetchAsync<T>(string key, string url, ConcurrentDictionary<string, CacheEntry> cache,
            string cacheFile, Func<Task<T>> fetch, Func<CacheEntry, T> get, Action<CacheEntry, T> set, bool persist) where T : class
        {
            var now = DateTime.UtcNow;

            if (cache.TryGetValue(key, out var entry) && entry.Expiry > now)
            {
                var value = get(entry);
                if (value != null) return value;
            }

            var result = await fetch().ConfigureAwait(false);

            var newEntry = entry ?? new CacheEntry();
            newEntry.Expiry = JikanHttpMetadataStore.TryGetExpiry(url, out var expiry)
                ? expiry
                : now.Add(BackupExpiry);

            set(newEntry, result);
            cache[key] = newEntry;

            if (persist)
                await SaveCacheAsync(cacheFile, cache).ConfigureAwait(false);

            return result;
        }

        private static string AnimeFullUrl(long id) => $"anime/{id}/full";
        private static string AnimeEpisodesUrl(long id) => $"anime/{id}/episodes";
        private static string AnimeCharactersUrl(long id) => $"anime/{id}/characters";
        private static string AnimePicturesUrl(long id) => $"anime/{id}/pictures";

        public static async Task<AnimeFullCacheDto> GetAnimeFullAsync(long malId, CancellationToken token, bool needRelations = false)
        {
            var key = malId.ToString();
            var now = DateTime.UtcNow;

            if (AnimeCache.TryGetValue(key, out var cached) && cached.Expiry > now)
            {
                if (!needRelations || cached.Anime?.Relations != null)
                    return cached.Anime;

                var relations = await Instance.GetAnimeRelationsAsync(malId, token);
                cached.Anime.Relations = RelatedEntryDto.FilterRelations(relations.Data);

                return cached.Anime;
            }

            var full = await Instance.GetAnimeFullDataAsync(malId, token);
            var dto = AnimeFullCacheDto.From(full.Data);

            var finished = dto?.Aired?.To;

            var expiry = (finished.HasValue && finished.Value < now.AddMonths(-1))
                ? now.Add(BackupExpiry)
                : JikanHttpMetadataStore.TryGetExpiry(AnimeFullUrl(malId), out var headerExp)
                    ? headerExp
                    : now.Add(SearchExpiry);

            AnimeCache[key] = new CacheEntry
            {
                Anime = dto,
                Expiry = expiry
            };

            await SaveCacheAsync(AnimeCacheFile, AnimeCache);
            return dto;
        }

        public static Task<EpisodeCacheDto> GetAnimeEpisodeAsync(long malId, int episodeNumber, CancellationToken token) =>
            GetOrFetchAsync(
                malId.ToString(),
                AnimeEpisodesUrl(malId),
                EpisodesCache,
                EpisodesCacheFile,
                async () =>
                {
                    var res = await Instance.GetAnimeEpisodeAsync(malId, episodeNumber, token);
                    return EpisodeCacheDto.From(res.Data);
                },
                e => e.Episodes != null && e.Episodes.TryGetValue(episodeNumber, out var ep) ? ep : null,
                (e, v) =>
                {
                    e.Episodes ??= new();
                    e.Episodes[episodeNumber] = v;
                },
                true);

        public static async Task<List<AnimeFullCacheDto>> SearchAnimeAsync(string term, CancellationToken token)
        {
            var now = DateTime.UtcNow;

            if (SearchCache.TryGetValue(term, out var cached) &&
                cached.Expiry > now &&
                cached.Ids != null)
            {
                var results = await Task.WhenAll(cached.Ids.Select(id => GetAnimeFullAsync(id, token)));
                return results.OrderBy(a => a.MalId).ToList();
            }

            var search = await Instance.SearchAnimeAsync(term, token);

            var valid = search.Data
                .Where(a => a.MalId.HasValue)
                .OrderBy(a => a.MalId.Value)
                .ToList();

            var anime = await Task.WhenAll(valid.Select(a =>
                GetOrFetchAsync(
                    a.MalId.Value.ToString(),
                    AnimeFullUrl(a.MalId.Value),
                    AnimeCache,
                    AnimeCacheFile,
                    () => Task.FromResult(AnimeFullCacheDto.From(a)),
                    e => e.Anime,
                    (e, v) => e.Anime = v,
                    false)));

            await SaveCacheAsync(AnimeCacheFile, AnimeCache);

            SearchCache[term] = new CacheEntry
            {
                Ids = valid.Select(a => a.MalId.Value).ToList(),
                Expiry = now.Add(SearchExpiry)
            };

            await SaveCacheAsync(SearchCacheFile, SearchCache);

            return anime.ToList();
        }

        public static Task<List<CharacterCacheDto>> GetAnimeCharactersAsync(long malId, CancellationToken token) =>
            GetOrFetchAsync(
                malId.ToString(),
                AnimeCharactersUrl(malId),
                CharactersCache,
                CharactersCacheFile,
                async () =>
                {
                    var res = await Instance.GetAnimeCharactersAsync(malId, token);
                    return res.Data.Select(CharacterCacheDto.From).ToList();
                },
                e => e.Characters,
                (e, v) => e.Characters = v,
                true);

        public static Task<List<ImagesSetDto>> GetAnimePicturesAsync(long malId, CancellationToken token) =>
            GetOrFetchAsync(
                malId.ToString(),
                AnimePicturesUrl(malId),
                PicturesCache,
                PicturesCacheFile,
                async () =>
                {
                    var res = await Instance.GetAnimePicturesAsync(malId, token);
                    return res.Data.Select(ImagesSetDto.From).Where(x => x != null).ToList();
                },
                e => e.Pictures,
                (e, v) => e.Pictures = v,
                true);
    }
}
