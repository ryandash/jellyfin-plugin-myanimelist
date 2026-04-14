using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using JikanDotNet;
using JikanDotNet.Config;
using LiteDB;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem
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

        private static ICacheStore Cache;

        private static TimeSpan BackupExpiry => TimeSpan.FromDays(Config.cacheBackupOtherTime);
        private static TimeSpan SearchExpiry => TimeSpan.FromMinutes(Config.cacheSearchTime);

        public static void Initialize(IApplicationPaths paths)
        {
            if (_initialized) return;

            lock (InitLock)
            {
                if (_initialized) return;
                _initialized = true;

                var baseDir = Path.Combine(paths.CachePath, "myanimelist");
                Directory.CreateDirectory(baseDir);

                Cache = new LiteDbCacheStore(baseDir, Config.DisableLocalCache);
            }
        }

        private static async Task<T> GetOrFetchAsync<T>(string key, string url, Func<Task<T>> fetch, Func<T, T> normalize = null) where T : class
        {
            var now = DateTime.UtcNow;

            var cached = Cache.Get<T>(key);
            if (cached != null)
                return cached;

            var result = await fetch().ConfigureAwait(false);

            if (normalize != null)
                result = normalize(result);

            var expiry = JikanHttpMetadataStore.TryGetExpiry(url, out var exp)
                ? exp
                : now.Add(BackupExpiry);

            Cache.Put(key, result, expiry);

            return result;
        }

        private static string AnimeFullUrl(long id) => $"anime/{id}/full";
        private static string AnimeEpisodesUrl(long id) => $"anime/{id}/episodes";
        private static string AnimeCharactersUrl(long id) => $"anime/{id}/characters";
        private static string CharacterUrl(long id) => $"characters/{id}";
        private static string AnimePicturesUrl(long id) => $"anime/{id}/pictures";
        private static readonly ConcurrentDictionary<long, SemaphoreSlim> RelationLocks = new();

        public static async Task<AnimeFullCacheDto> GetAnimeFullAsync(long malId, CancellationToken token, bool needRelations = false)
        {
            var key = $"anime:{malId}";

            var cached = Cache.Get<AnimeFullCacheDto>(key);
            if (cached != null)
            {
                if (!needRelations || cached.Relations != null)
                    return cached;

                var gate = RelationLocks.GetOrAdd(malId, _ => new SemaphoreSlim(1, 1));
                await gate.WaitAsync(token).ConfigureAwait(false);

                try
                {
                    if (cached.Relations != null)
                        return cached;

                    var relations = await Instance.GetAnimeRelationsAsync(malId, token);
                    cached.Relations = RelatedEntryDto.FilterRelations(relations.Data);

                    Cache.Put(key, cached, DateTime.UtcNow.Add(BackupExpiry));

                    return cached;
                }
                finally
                {
                    gate.Release();
                }
            }

            var full = await Instance.GetAnimeFullDataAsync(malId, token);
            var dto = AnimeFullCacheDto.From(full.Data);

            var now = DateTime.UtcNow;
            var expiry =
                JikanHttpMetadataStore.TryGetExpiry(AnimeFullUrl(malId), out var headerExp)
                    ? headerExp
                    : now.Add(SearchExpiry);

            Cache.Put(key, dto, expiry);

            return dto;
        }

        public static Task<EpisodeCacheDto> GetAnimeEpisodeAsync(long malId, int episodeNumber, CancellationToken token)
        {
            var key = $"episode:{malId}:{episodeNumber}";

            return GetOrFetchAsync(
                key,
                AnimeEpisodesUrl(malId),
                async () =>
                {
                    var res = await Instance.GetAnimeEpisodeAsync(malId, episodeNumber, token);
                    return EpisodeCacheDto.From(res.Data);
                }
            );
        }

        public static async Task<List<AnimeFullCacheDto>> SearchAnimeAsync(string term, CancellationToken token)
        {
            var key = $"search:{term}";

            var cached = Cache.Get<List<long>>(key);
            if (cached != null)
            {
                var results = await Task.WhenAll(
                    cached.Select(id => GetAnimeFullAsync(id, token, false))
                );

                return results.OrderBy(x => x.MalId).ToList();
            }

            var search = await Instance.SearchAnimeAsync(term, token);

            var ids = search.Data
                .Where(a => a.MalId.HasValue)
                .OrderBy(a => a.MalId.Value)
                .ToList();

            var anime = await Task.WhenAll(ids.Select(a =>
                    GetOrFetchAsync(
                        $"anime:{a.MalId}",
                        AnimeFullUrl(a.MalId.Value),
                        async () =>
                        {
                            return AnimeFullCacheDto.From(a);
                        }
                    )
                )
            );

            Cache.Put(key, ids.Select(a => a.MalId.Value).ToList(), DateTime.UtcNow.Add(SearchExpiry));

            return anime.ToList();
        }

        public static async Task<List<AnimeCharacterDto>> GetAnimeCharactersAsync(long malId, CancellationToken token)
        {
            var key = $"characters:{malId}";

            var cached = Cache.Get<List<AnimeCharacterIdCacheDto>>(key);
            if (cached != null)
            {
                var tasks = cached.Select(async r =>
                {
                    var character = await GetOrFetchAsync(
                        $"character:{r.CharacterId}",
                        CharacterUrl(r.CharacterId),
                        async () =>
                        {
                            var res = await Instance.GetCharacterAsync(r.CharacterId, token);
                            return CharacterCacheDto.From(res.Data);
                        }
                    );

                    return new AnimeCharacterDto
                    {
                        Character = character,
                        Role = r.Role,
                        VoiceActors = r.VoiceActors
                    };
                });

                return (await Task.WhenAll(tasks)).ToList();
            }

            var res = await Instance.GetAnimeCharactersAsync(malId, token);
            var animeCharacters = res.Data;

            var expiry = JikanHttpMetadataStore.TryGetExpiry(AnimeCharactersUrl(malId), out var exp)
                ? exp
                : DateTime.UtcNow.Add(BackupExpiry);

            foreach (AnimeCharacter cha in animeCharacters)
            {
                if (cha?.Character?.MalId > 0)
                {
                    var character = CharacterCacheDto.From(cha.Character);
                    if (character != null)
                    {
                        Cache.Put($"character:{cha.Character.MalId}", character, expiry);
                    }
                }
            }

            Cache.Put(key, animeCharacters.Select(AnimeCharacterIdCacheDto.From).Where(x => x != null)
                .OrderBy(x => x.CharacterId).ToList(), expiry);

            return animeCharacters.Select(AnimeCharacterDto.From).ToList();
        }

        public static Task<List<ImagesSetDto>> GetAnimePicturesAsync(long malId, CancellationToken token)
        {
            var key = $"pictures:{malId}";

            return GetOrFetchAsync(
                key,
                AnimePicturesUrl(malId),
                async () =>
                {
                    var res = await Instance.GetAnimePicturesAsync(malId, token);
                    return res.Data
                        .Select(ImagesSetDto.From)
                        .Where(x => x != null)
                        .ToList();
                }
            );
        }
    }
}
