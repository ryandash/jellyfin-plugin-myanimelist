using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using JikanDotNet;
using JikanDotNet.Config;
using JikanDotNet.Exceptions;
using LiteDB;
using MediaBrowser.Common.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem
{
    public static class JikanAPI
    {
        private static readonly object InitLock = new();
        private static bool _initialized;

        private static Jikan CreateClient(string baseUrl)
        {
            var http = new HttpClient(new JikanHeaderHandler(new HttpClientHandler()))
            {
                BaseAddress = new Uri(baseUrl)
            };

            return new Jikan(new JikanClientConfiguration(), http);
        }

        private static readonly Lazy<Jikan> PrimaryJikan =
            new(() => CreateClient("http://jikanapi.freemyip.com:8080/v4/"));

        private static readonly Lazy<Jikan> BackupJikan =
            new(() => CreateClient("https://api.jikan.moe/v4/"));
        private static async Task<T> TryPrimaryThenBackup<T>(Func<Jikan, Task<T>> action)
        {
            try
            {
                return await action(PrimaryJikan.Value);
            }
            catch
            {
                return await action(BackupJikan.Value);
            }
        }

        private static ICacheStore Cache;

        private static TimeSpan BackupExpiry;
        private static TimeSpan SearchExpiry;

        private static PluginConfiguration _config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public static void Initialize(IApplicationPaths paths)
        {
            if (_initialized) return;

            lock (InitLock)
            {
                if (_initialized) return;
                _initialized = true;

                var baseDir = Path.Combine(paths.CachePath, "myanimelist");
                Directory.CreateDirectory(baseDir);

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

                if (cached is not null)
                    return cached;
            }

            var lazyTask = _inFlight.GetOrAdd(
                key,
                _ => new Lazy<Task<object>>(() => FetchAndCache(), LazyThreadSafetyMode.ExecutionAndPublication)
            );

            try
            {
                var result = await lazyTask.Value.ConfigureAwait(false);

                return result is TCache typed ? typed : null;
            }
            finally
            {
                _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<object>>>(key, lazyTask));
            }

            async Task<object> FetchAndCache()
            {
                const int maxAttempts = 2;

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        var apiResult = await fetch().ConfigureAwait(false);

                        if (apiResult is not null)
                        {
                            var normalized = normalize(apiResult);

                            if (normalized is not null)
                            {
                                var expiry = JikanHttpMetadataStore.TryGetExpiry(url, out var exp)
                                    ? exp
                                    : DateTime.UtcNow.Add(BackupExpiry);

                                Cache.Put(key, normalized, expiry);

                                return normalized;
                            }
                        }
                    }
                    catch (JikanRequestException ex) when (ex.ApiError?.Status == HttpStatusCode.ServiceUnavailable)
                    {
                        break;
                    }
                    catch (JikanRequestException ex) when (ex.ApiError?.Status == HttpStatusCode.InternalServerError)
                    {
                        if (attempt < maxAttempts)
                        {
                            await Task.Delay(TimeSpan.FromMinutes(5)).ConfigureAwait(false);
                        }
                    }
                    catch (HttpRequestException)
                    {
                        if (attempt < maxAttempts)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                        }
                    }
                }

                return null;
            }
        }

        private static string AnimeFullUrl(long id) => $"anime/{id}/full";
        private static string AnimeSpecificEpisodesUrl(long id, int ep) => $"anime/{id}/episodes/{ep}";
        private static string AnimeEpisodesUrl(long id) => $"anime/{id}/episodes";
        private static string AnimeCharactersUrl(long id) => $"anime/{id}/characters";
        private static string CharacterUrl(long id) => $"characters/{id}";
        private static string PeopleUrl(long id) => $"people/{id}";

        public static async Task<AnimeFullCacheDto> GetAnimeFullAsync(long malId, CancellationToken token, bool needRelations = false)
        {
            var key = $"anime:{malId}";

            var cached = Cache.Get<AnimeFullCacheDto>(key);

            if (cached is not null &&
                (!needRelations || cached.Relations is not null))
            {
                return cached;
            }

            return await GetOrFetchAsync(
                key,
                AnimeFullUrl(malId),

                fetch: () => TryPrimaryThenBackup(j => j.GetAnimeFullDataAsync(malId, token)),

                normalize: res => AnimeFullCacheDto.From(res.Data),

                ignoreCache: cached is not null
            );
        }

        public static async Task<EpisodeCacheDto> GetAnimeEpisodeAsync(long malId, int episodeNumber, CancellationToken token)
        {
            var key = $"episode:{malId}:{episodeNumber}";

            var cached = Cache.Get<EpisodeCacheDto>(key);

            if (cached is not null && cached.HasFullDetails)
            {
                return cached;
            }

            var detailed = await GetOrFetchAsync(
                key,
                AnimeSpecificEpisodesUrl(malId, episodeNumber),

                fetch: () => TryPrimaryThenBackup(j => j.GetAnimeEpisodeAsync(malId, episodeNumber, token)),

                normalize: res => EpisodeCacheDto.From(res.Data),

                ignoreCache: true
            );

            // Merge cached and detailed data. If either is null, the merge will return the non-null one
            var merged = EpisodeCacheDto.MergeEpisodeDetails(cached, detailed);

            if (merged is not null)
            {
                var expiry = JikanHttpMetadataStore.TryGetExpiry(AnimeSpecificEpisodesUrl(malId, episodeNumber), out var exp)
                    ? exp
                    : DateTime.UtcNow.Add(BackupExpiry);

                Cache.Put(key, merged, expiry);

                return merged;
            }

            return null;
        }

        public static Task<List<EpisodeCacheDto>> GetAnimeEpisodesAsync(long malId, CancellationToken token)
        {
            var key = $"episodes:{malId}";

            return GetOrFetchAsync(
                key,
                AnimeEpisodesUrl(malId),

                fetch: async () =>
                {
                    var allEpisodes = new List<AnimeEpisode>();

                    int page = 1;

                    while (true)
                    {
                        var res = await TryPrimaryThenBackup(j => j.GetAnimeEpisodesAsync(malId, page, token));

                        if (res?.Data is null || res.Data.Count == 0)
                            break;

                        allEpisodes.AddRange(res.Data);

                        if (res.Pagination?.HasNextPage != true)
                            break;

                        page++;
                    }

                    return allEpisodes;
                },

                normalize: res =>
                {
                    if (res.Count == 0)
                        return null;

                    var episodes = res.Select(EpisodeCacheDto.From).Where(x => x is not null).ToList();

                    foreach (var ep in episodes)
                    {
                        var expiry = JikanHttpMetadataStore.TryGetExpiry(AnimeSpecificEpisodesUrl(malId, ep.EpisodeNumber), out var exp)
                            ? exp
                            : DateTime.UtcNow.Add(BackupExpiry);

                        Cache.Put(
                            $"episode:{malId}:{ep.EpisodeNumber}",
                            ep,
                            expiry);
                    }

                    return episodes;
                }
            );
        }

        public static Task<PersonDto> getPersonAsync(long malId, CancellationToken token)
        {
            var key = $"person:{malId}";

            return GetOrFetchAsync(
                key,
                PeopleUrl(malId),

                fetch: () => TryPrimaryThenBackup(j => j.GetPersonAsync(malId, token)),

                normalize: res => PersonDto.From(res.Data)
            );
        }

        public static async Task<List<AnimeFullCacheDto>> SearchAnimeAsync(string term, CancellationToken token)
        {
            var key = $"search:{term}";

            var cached = Cache.Get<List<long>>(key);
            if (cached is not null)
            {
                var results = await Task.WhenAll(
                    cached.Select(id => GetAnimeFullAsync(id, token, false))
                );

                return results.OrderBy(x => x.MalId).ToList();
            }

            AnimeSearchConfig searchConfig = new AnimeSearchConfig
            {
                Query = term,
                Page = 1
            };
            var search = await TryPrimaryThenBackup(j => j.SearchAnimeAsync(searchConfig, token));

            var ids = search.Data.Where(a => a.MalId.HasValue).Select(a => (anime: a, id: a.MalId.Value)).ToList();

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

            return anime.OrderBy(a => a.MalId.Value).ToList();
        }

        private static Task<List<AnimeCharacterIdCacheDto>> GetAnimeCharacterIndexAsync(long malId, CancellationToken token)
        {
            var key = $"characters:{malId}";

            return GetOrFetchAsync(
                key,
                AnimeCharactersUrl(malId),

                fetch: () => TryPrimaryThenBackup(j => j.GetAnimeCharactersAsync(malId, token)),

                normalize: res =>
                {
                    var expiry = JikanHttpMetadataStore.TryGetExpiry(AnimeCharactersUrl(malId), out var exp)
                        ? exp
                        : DateTime.UtcNow.Add(BackupExpiry);

                    if (res?.Data is null) return null;

                    foreach (var cha in res.Data)
                    {
                        CharacterEntry character = cha.Character;
                        if (character.MalId > 0)
                        {
                            var characterCache = CharacterCacheDto.From(character);

                            if (characterCache is not null)
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

                                if (personCache is not null)
                                {
                                    Cache.Put(
                                        $"person:{person.MalId}",
                                        personCache,
                                        expiry);
                                }
                            }
                        }
                    }

                    return res.Data.Select(AnimeCharacterIdCacheDto.From).Where(x => x is not null).OrderBy(x => x.CharacterId).ToList();
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

                    fetch: () => TryPrimaryThenBackup(j => j.GetCharacterAsync(charID, token)),

                    normalize: res => CharacterCacheDto.From(res.Data)
                );

                var voiceActors = new List<VoiceActorEntryDto>();
                foreach (var va in r.VoiceActors)
                {
                    var personId = va.Person.MalId;
                    var person = await GetOrFetchAsync(
                        $"person:{personId}",
                        PeopleUrl(personId),

                        fetch: () => TryPrimaryThenBackup(j => j.GetPersonAsync(personId, token)),

                        normalize: res => PersonDto.From(res.Data)
                    );

                    if (person is not null)
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

            return (await Task.WhenAll(tasks)).Where(x => x?.Character is not null).ToList();
        }

        public static async Task<List<AnimeCharacterDto>> GetAnimeCharactersAsync(long malId, CancellationToken token)
        {
            var cached = Cache.Get<List<AnimeCharacterIdCacheDto>>($"characters:{malId}");

            if (cached is not null)
            {
                return await HydrateCharactersAsync(cached, token);
            }

            var index = await GetAnimeCharacterIndexAsync(malId, token);

            if (index is null)
                return new List<AnimeCharacterDto>();

            var expiry = JikanHttpMetadataStore.TryGetExpiry(AnimeCharactersUrl(malId), out var exp)
                ? exp
                : DateTime.UtcNow.Add(BackupExpiry);

            Cache.Put(
                $"characters:{malId}",
                index,
                expiry);

            return await HydrateCharactersAsync(index, token);
        }
    }
}
