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
using JikanDotNet.Exceptions;
using LiteDB;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem
{
    public static class JikanAPI
    {
        private static readonly object InitLock = new();
        private static bool _initialized;

        private static readonly HttpClient HttpClient =
            new(new JikanHeaderHandler(new HttpClientHandler()))
            {
                BaseAddress = new Uri("https://api.jikan.moe/v4/")
            };

        private static readonly Lazy<Jikan> JikanInstance =
            new(() => new Jikan(new JikanClientConfiguration(), HttpClient));

        private static Jikan Instance => JikanInstance.Value;

        private static ICacheStore Cache;

        private static TimeSpan BackupExpiry;
        private static TimeSpan SearchExpiry;

        public static void Initialize(IApplicationPaths paths)
        {
            if (_initialized) return;

            lock (InitLock)
            {
                if (_initialized) return;
                _initialized = true;

                var baseDir = Path.Combine(paths.CachePath, "myanimelist");
                Directory.CreateDirectory(baseDir);

                var _config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

                BackupExpiry = TimeSpan.FromDays(_config.cacheBackupOtherTime);
                SearchExpiry = TimeSpan.FromMinutes(_config.cacheSearchTime);

                Cache = new LiteDbCacheStore(baseDir, _config.DisableLocalCache);
            }
        }

        private static readonly ConcurrentDictionary<string, Lazy<Task<object>>> _inFlight = new();

        private static async Task<TCache> GetOrFetchAsync<TApi, TCache>(string key, string url, Func<Task<TApi>> fetch, Func<TApi, TCache> normalize, bool ignoreCache = false) where TCache : class
        {

            if (!ignoreCache)
            {
                var cached = Cache.Get<TCache>(key);

                if (cached != null)
                    return cached;
            }

            var lazyTask = _inFlight.GetOrAdd(
                key,
                _ => new Lazy<Task<object>>(
                    () => FetchAndCache(),
                    LazyThreadSafetyMode.ExecutionAndPublication)
            );

            try
            {
                var result = await lazyTask.Value.ConfigureAwait(false);

                return result is TCache typed ? typed : null;
            }
            finally
            {
                _inFlight.TryRemove(
                    new KeyValuePair<string, Lazy<Task<object>>>(key, lazyTask));
            }

            async Task<object> FetchAndCache()
            {
                const int maxAttempts = 3;

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        var apiResult = await fetch().ConfigureAwait(false);

                        if (apiResult != null)
                        {
                            var normalized = normalize(apiResult);

                            if (normalized != null)
                            {
                                var expiry =
                                    JikanHttpMetadataStore.TryGetExpiry(url, out var exp)
                                        ? exp
                                        : DateTime.UtcNow.Add(BackupExpiry);

                                Cache.Put(key, normalized, expiry);

                                return normalized;
                            }
                        }
                    }
                    catch (JikanRequestException)
                    {
                    }
                    catch (HttpRequestException)
                    {
                    }

                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(
                            TimeSpan.FromSeconds(attempt * 2))
                            .ConfigureAwait(false);
                    }
                }

                return null;
            }
        }

        private static string AnimeFullUrl(long id) => $"anime/{id}/full";
        private static string AnimeEpisodesUrl(long id) => $"anime/{id}/episodes";
        private static string AnimeCharactersUrl(long id) => $"anime/{id}/characters";
        private static string CharacterUrl(long id) => $"characters/{id}";
        private static string AnimePicturesUrl(long id) => $"anime/{id}/pictures";
        private static string PeopleUrl(long id) => $"people/{id}";

        public static async Task<AnimeFullCacheDto> GetAnimeFullAsync(long malId, CancellationToken token, bool needRelations = false)
        {
            var key = $"anime:{malId}";

            var cached = Cache.Get<AnimeFullCacheDto>(key);

            if (cached != null &&
                (!needRelations || cached.Relations != null))
            {
                return cached;
            }

            return await GetOrFetchAsync(
                key,
                AnimeFullUrl(malId),

                fetch: () => Instance.GetAnimeFullDataAsync(malId, token),

                normalize: res => AnimeFullCacheDto.From(res.Data),

                ignoreCache: cached != null
            );
        }

        public static Task<EpisodeCacheDto> GetAnimeEpisodeAsync(long malId, int episodeNumber, CancellationToken token)
        {
            var key = $"episode:{malId}:{episodeNumber}";

            return GetOrFetchAsync(
                key,
                AnimeEpisodesUrl(malId),

                fetch: () => Instance.GetAnimeEpisodeAsync(
                    malId,
                    episodeNumber,
                    token),

                normalize: res => EpisodeCacheDto.From(res.Data)
            );
        }

        public static Task<List<ImagesSetDto>> GetAnimePicturesAsync(long malId, CancellationToken token)
        {
            var key = $"pictures:{malId}";

            return GetOrFetchAsync(
                key,
                AnimePicturesUrl(malId),

                fetch: () => Instance.GetAnimePicturesAsync(malId, token),

                normalize: res => res.Data
                    .Select(ImagesSetDto.From)
                    .Where(x => x != null)
                    .ToList()
            );
        }

        public static Task<PersonDto> getPersonAsync(long malId, CancellationToken token)
        {
            var key = $"person:{malId}";

            return GetOrFetchAsync(
                key,
                PeopleUrl(malId),

                fetch: () => Instance.GetPersonAsync(malId, token),

                normalize: res => PersonDto.From(res.Data)
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
                .Select(a => (anime: a, id: a.MalId.Value))
                .ToList();

            var anime = await Task.WhenAll(ids.Select(a =>
                    GetOrFetchAsync(
                        $"anime:{a.id}",
                        AnimeFullUrl(a.id),

                        fetch: () => Task.FromResult(a.anime),

                        normalize: AnimeFullCacheDto.From
                    )
                )
            );

            Cache.Put(key, ids.Select(a => a.id).ToList(), DateTime.UtcNow.Add(SearchExpiry));

            return anime.ToList();
        }

        private static Task<List<AnimeCharacterIdCacheDto>> GetAnimeCharacterIndexAsync(long malId, CancellationToken token)
        {
            var key = $"characters:{malId}";

            return GetOrFetchAsync(
                key,
                AnimeCharactersUrl(malId),

                fetch: () => Instance.GetAnimeCharactersAsync(malId, token),

                normalize: res =>
                {
                    var expiry =
                        JikanHttpMetadataStore.TryGetExpiry(AnimeCharactersUrl(malId), out var exp)
                            ? exp
                            : DateTime.UtcNow.Add(BackupExpiry);

                    foreach (var cha in res.Data)
                    {
                        CharacterEntry character = cha.Character;
                        if (character.MalId > 0)
                        {
                            var characterCache = CharacterCacheDto.From(character);

                            if (characterCache != null)
                            {
                                Cache.Put(
                                    $"character:{character.MalId}",
                                    characterCache,
                                    expiry);

                            }
                        }

                        foreach (var va in cha.VoiceActors)
                        {
                            MalImageSubItem person = va.Person;
                            if (person.MalId > 0)
                            {
                                var personCache = PersonDto.From(va.Person);

                                if (personCache != null)
                                {
                                    Cache.Put(
                                        $"person:{person.MalId}",
                                        personCache,
                                        expiry);
                                }
                            }
                        }
                    }

                    return res.Data
                        .Select(AnimeCharacterIdCacheDto.From)
                        .Where(x => x != null)
                        .OrderBy(x => x.CharacterId)
                        .ToList();
                });
        }

        private static async Task<List<AnimeCharacterDto>> HydrateCharactersAsync(List<AnimeCharacterIdCacheDto> cached, CancellationToken token)
        {
            var tasks = cached.Select(async r =>
            {
                var charID = r.CharacterId;
                var character = await GetOrFetchAsync(
                    $"character:{charID}",
                    CharacterUrl(charID),

                    fetch: () => Instance.GetCharacterAsync(
                        charID,
                        token),

                    normalize: res => CharacterCacheDto.From(res.Data)
                );

                var voiceActors = new List<VoiceActorEntryDto>();
                foreach (var va in r.VoiceActors)
                {
                    var personId = va.Person.MalId;
                    var person = await GetOrFetchAsync(
                        $"person:{personId}",
                        PeopleUrl(personId),

                        fetch: () => Instance.GetPersonAsync(personId, token),

                        normalize: res => PersonDto.From(res.Data)
                    );

                    if (person != null)
                    {
                        voiceActors.Add(new VoiceActorEntryDto
                        {
                            Language = va.Language,
                            Person = person
                        });
                    }
                }

                return new AnimeCharacterDto
                {
                    Character = character,
                    Role = r.Role,
                    VoiceActors = voiceActors
                };
            });

            return (await Task.WhenAll(tasks))
                .Where(x => x?.Character != null)
                .ToList();
        }

        public static async Task<List<AnimeCharacterDto>> GetAnimeCharactersAsync(long malId, CancellationToken token)
        {
            var cached = Cache.Get<List<AnimeCharacterIdCacheDto>>($"characters:{malId}");

            if (cached != null)
            {
                return await HydrateCharactersAsync(cached, token);
            }

            var index = await GetAnimeCharacterIndexAsync(malId, token);

            if (index == null)
                return new List<AnimeCharacterDto>();

            Cache.Put(
                $"characters:{malId}",
                index,
                DateTime.UtcNow.Add(BackupExpiry));

            return await HydrateCharactersAsync(index, token);
        }
    }
}
