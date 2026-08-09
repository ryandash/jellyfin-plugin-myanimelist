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
        private static bool _initialized;
        private static readonly object InitLock = new();
        private static ICacheStore Cache;
        private static Jikan _primaryClient;
        private static Jikan _backupClient;
        private static string _primaryUrl;
        private static string _backupUrl;
        private static readonly PluginConfiguration DefaultConfig = new();
        private static PluginConfiguration _config => Plugin.Instance?.Configuration ?? DefaultConfig;
        private static readonly string defaultPrimaryBaseUrl = "api.tenrai.org";
        private static readonly string defaultPrimary = $"https://{defaultPrimaryBaseUrl}/v1/";
        private static readonly string defaultBackup = "https://jikanapi.freemyip.com/v4/";
        private static TimeSpan BackupExpiry => TimeSpan.FromDays(_config.CacheBackupOtherTime);
        private static TimeSpan SearchExpiry => TimeSpan.FromMinutes(_config.CacheSearchTime);

        private static readonly ConcurrentDictionary<string, Lazy<Task<object>>> _inFlight = new();

        private static DateTime GetExpiry(string url)
        {
            return JikanHttpMetadataStore.TryGetExpiry(url, out var exp)
                ? exp
                : DateTime.UtcNow.Add(BackupExpiry);
        }

        private sealed class JikanResult<T>
        {
            public T Data { get; init; }
            public bool IsTenrai { get; init; }
        }

        public static void Initialize(IApplicationPaths paths)
        {
            if (_initialized) return;

            lock (InitLock)
            {
                if (_initialized) return;
                _initialized = true;

                var cachePath = Path.Combine(paths.CachePath, "myanimelist");
                Directory.CreateDirectory(cachePath);

                Cache = new LiteDbCacheStore(cachePath);
            }
        }

        private static Jikan CreateClient(string baseUrl)
        {
            var http = new HttpClient(new JikanHeaderHandler(new HttpClientHandler()))
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromMinutes(5)
            };

            return new Jikan(new JikanClientConfiguration(), http);
        }

        private static void EnsureClients()
        {
            var p = string.IsNullOrWhiteSpace(_config.PrimaryJikanUrl)
                ? defaultPrimary
                : _config.PrimaryJikanUrl;
            var b = string.IsNullOrWhiteSpace(_config.BackupJikanUrl)
                ? defaultBackup
                : _config.BackupJikanUrl;

            if (_primaryClient == null || _backupClient == null ||
                p != _primaryUrl || b != _backupUrl)
            {
                _primaryClient = CreateClient(p);
                _backupClient = CreateClient(b);

                _primaryUrl = p;
                _backupUrl = b;
            }
        }

        private static bool IsTenrai(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return string.Equals(
                uri.Host,
                defaultPrimaryBaseUrl,
                StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<JikanResult<T>> TryPrimaryThenBackup<T>(Func<Jikan, Task<T>> action)
        {
            EnsureClients();

            try
            {
                return new JikanResult<T>
                {
                    Data = await action(_primaryClient),
                    IsTenrai = IsTenrai(_primaryUrl)
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                try
                {
                    return new JikanResult<T>
                    {
                        Data = await action(_backupClient),
                        IsTenrai = IsTenrai(_backupUrl)
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
        }

        // Handles fetching data with caching and in-flight request deduplication
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
                                Cache.Put(key, normalized, GetExpiry(url));

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

        // Handles fetching new data to merge with incomplete if cached data is not complete
        private static async Task<TCache> GetMergedAsync<TApi, TCache>(string key, string url, Func<Task<TApi>> fetch, Func<TApi, TCache> normalize, Func<TCache, TCache, TCache> merge, Func<TCache, bool> isComplete) where TCache : class
        {
            var cached = Cache.Get<TCache>(key);

            if (cached != null && isComplete(cached))
                return cached;

            var detailed = await GetOrFetchAsync(
                key,
                url,
                fetch,
                normalize,
                ignoreCache: true);

            var merged = merge(cached, detailed);

            if (merged != null)
            {
                Cache.Put(key, merged, GetExpiry(url));
            }

            return merged;
        }

        // API METHODS START

        // BASIC GET METHODS
        public static async Task<AnimeFullCacheDto> GetAnimeFullAsync(long malId, CancellationToken token, bool needRelations = false)
        {
            var key = Keys.AnimeFull(malId);

            var cached = Cache.Get<AnimeFullCacheDto>(key);

            if (cached is not null && (!needRelations || cached.Relations is not null))
            {
                return cached;
            }

            return await GetOrFetchAsync(
                key,
                URLs.AnimeFull(malId),

                fetch: () => TryPrimaryThenBackup(j => j.GetAnimeFullDataAsync(malId, token)),

                normalize: a => AnimeFullCacheDto.From(a.Data.Data),

                ignoreCache: cached is not null
            );
        }

        public static Task<EpisodeCacheDto> GetAnimeEpisodeAsync(long malId, int episodeNumber, CancellationToken token)
        {
            return GetMergedAsync(
                Keys.AnimeEpisode(malId, episodeNumber),
                URLs.AnimeSpecificEpisodes(malId, episodeNumber),

                fetch: () => TryPrimaryThenBackup(j => j.GetAnimeEpisodeAsync(malId, episodeNumber, token)),

                normalize: e => EpisodeCacheDto.From(e.Data.Data),

                merge: EpisodeCacheDto.MergeEpisodeDetails,

                isComplete: e => e.HasFullDetails
            );
        }

        public static Task<PersonDto> GetPersonAsync(long malId, CancellationToken token)
        {
            return GetMergedAsync(
                Keys.Person(malId),
                URLs.People(malId),

                fetch: () => TryPrimaryThenBackup(j => j.GetPersonAsync(malId, token)),

                normalize: p => PersonDto.From(p.Data.Data),

                merge: PersonDto.MergePersonDetails,

                isComplete: p => p.HasFullDetails
            );
        }

        public static Task<CharacterCacheDto> GetCharacterAsync(long malId, CancellationToken token)
        {
            return GetMergedAsync(
                Keys.Character(malId),
                URLs.Character(malId),

                fetch: () => TryPrimaryThenBackup(j => j.GetCharacterAsync(malId, token)),

                normalize: c => CharacterCacheDto.From(c.Data.Data),

                merge: CharacterCacheDto.MergeCharacterDetails,

                isComplete: c => c.HasFullDetails);
        }

        // SEARCH METHODS
        public static async Task<List<AnimeFullCacheDto>> SearchAnimeAsync(string query, bool nsfw, bool isMovie, CancellationToken token)
        {
            var key = Keys.SearchAnime(query, nsfw);

            var cached = Cache.Get<List<long>>(key);
            if (cached is not null)
            {
                var results = await Task.WhenAll(
                    cached.Select(id => GetAnimeFullAsync(id, token, false))
                );

                return results.ToList();
            }

            AnimeSearchConfig searchConfig = new AnimeSearchConfig
            {
                Query = query,
                Page = 1,
                Sfw = !nsfw,
                Type = isMovie ? AnimeType.Movie : AnimeType.EveryType,
            };
            var search = await TryPrimaryThenBackup(j => j.SearchAnimeAsync(searchConfig, token));
            var ids = search.Data.Data.Where(a => a.MalId.HasValue).Select(a => (anime: a, id: a.MalId.Value)).ToList();
            var anime = await Task.WhenAll(ids.Select(a =>
                    GetOrFetchAsync(
                        Keys.AnimeFull(a.id),
                        URLs.AnimeFull(a.id),

                        fetch: () => Task.FromResult(a.anime),

                        normalize: AnimeFullCacheDto.From
                    )
                )
            );

            Cache.Put(key, ids.Select(a => a.id).ToList(), DateTime.UtcNow.Add(SearchExpiry));

            return anime.ToList();
        }

        public static async Task<List<CharacterCacheDto>> SearchCharacterAsync(string term, CancellationToken token)
        {
            var key = Keys.SearchCharacter(term);

            var cached = Cache.Get<List<long>>(key);
            if (cached is not null)
            {
                var results = await Task.WhenAll(
                    cached.Select(id => GetCharacterAsync(id, token))
                );

                return results.ToList();
            }

            CharacterSearchConfig searchConfig = new CharacterSearchConfig
            {
                Query = term,
                Page = 1
            };
            var search = await TryPrimaryThenBackup(j => j.SearchCharacterAsync(searchConfig, token));
            var ids = search.Data.Data.Select(a => (character: a, id: a.MalId)).ToList();
            var characters = await Task.WhenAll(ids.Select(a =>
                    GetOrFetchAsync(
                        Keys.Character(a.id),
                        URLs.Character(a.id),

                        fetch: () => Task.FromResult(a.character),

                        normalize: c => CharacterCacheDto.From(c)
                    )
                )
            );

            Cache.Put(key, ids.Select(a => a.id).ToList(), DateTime.UtcNow.Add(SearchExpiry));

            return characters.ToList();
        }

        public static async Task<List<PersonDto>> SearchPersonAsync(string term, CancellationToken token)
        {
            var key = Keys.SearchPerson(term);
            var cached = Cache.Get<List<long>>(key);
            if (cached is not null)
            {
                var results = await Task.WhenAll(
                    cached.Select(id => GetPersonAsync(id, token))
                );
                return results.ToList();
            }
            PersonSearchConfig searchConfig = new PersonSearchConfig
            {
                Query = term,
                Page = 1
            };
            var search = await TryPrimaryThenBackup(j => j.SearchPersonAsync(searchConfig, token));
            var ids = search.Data.Data.Select(a => (person: a, id: a.MalId)).ToList();
            var people = await Task.WhenAll(ids.Select(a =>
                    GetOrFetchAsync(
                        Keys.Person(a.id),
                        URLs.People(a.id),
                        fetch: () => Task.FromResult(a.person),
                        normalize: p => PersonDto.From(p)
                    )
                )
            );
            Cache.Put(key, ids.Select(a => a.id).ToList(), DateTime.UtcNow.Add(SearchExpiry));

            return people.ToList();
        }

        // MORE ADVANCED GET METHODS
        public static Task<List<EpisodeCacheDto>> GetAnimeEpisodesAsync(long malId, CancellationToken token)
        {
            var key = Keys.AnimeEpisodes(malId);

            return GetOrFetchAsync(
                key,
                URLs.AnimeEpisodes(malId),

                fetch: async () =>
                {
                    var allEpisodes = new List<AnimeEpisode>();

                    int page = 1;

                    while (true)
                    {
                        var res = (await TryPrimaryThenBackup(j => j.GetAnimeEpisodesAsync(malId, page, token))).Data;

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
                        var expiry = GetExpiry(URLs.AnimeSpecificEpisodes(malId, ep.EpisodeNumber));

                        Cache.Put(Keys.AnimeEpisode(malId, ep.EpisodeNumber), ep, expiry);
                    }

                    return episodes;
                }
            );
        }

        private static Task<List<AnimeCharacterIdCacheDto>> GetAnimeCharacterIndexAsync(long malId, CancellationToken token)
        {
            var key = Keys.Characters(malId);

            return GetOrFetchAsync(
                key,
                URLs.AnimeCharacters(malId),

                fetch: () => TryPrimaryThenBackup(j => j.GetAnimeCharactersAsync(malId, token)),

                normalize: res =>
                {
                    var expiry = GetExpiry(URLs.AnimeCharacters(malId));

                    if (res?.Data.Data is null) return null;

                    foreach (var cha in res.Data.Data)
                    {
                        CharacterEntry character = cha.Character;
                        if (character.MalId > 0)
                        {
                            var characterCache = CharacterCacheDto.From(character);

                            if (characterCache is not null)
                            {
                                Cache.Put(Keys.Character(character.MalId), characterCache, expiry);
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
                                    Cache.Put(Keys.Person(person.MalId), personCache, expiry);
                                }
                            }
                        }
                    }

                    return res.Data.Data.Select(c => AnimeCharacterIdCacheDto.From(c)).Where(x => x is not null).OrderBy(x => x.CharacterId).ToList();
                });
        }

        private static async Task<List<AnimeCharacterDto>> HydrateCharactersAsync(List<AnimeCharacterIdCacheDto> cached, CancellationToken token)
        {
            var tasks = cached.Select(async r =>
            {
                var charID = r.CharacterId;
                var character = await GetOrFetchAsync(
                    Keys.Character(charID),
                    URLs.Character(charID),

                    fetch: () => TryPrimaryThenBackup(j => j.GetCharacterAsync(charID, token)),

                    normalize: res => CharacterCacheDto.From(res.Data.Data)
                );

                var voiceActors = await Task.WhenAll(
                    r.VoiceActors.Select(async va =>
                    {
                        var person = await GetPersonAsync(
                            va.Person.MalId,
                            token);

                        return person == null
                            ? null
                            : new VoiceActorEntryDto
                            {
                                Language = va.Language,
                                Person = person
                            };
                    }));

                return new AnimeCharacterDto
                {
                    Character = character,
                    Role = r.Role,
                    VoiceActors = voiceActors.Where(x => x != null).ToList()
                };
            });

            return (await Task.WhenAll(tasks)).Where(x => x?.Character is not null).ToList();
        }

        public static async Task<List<AnimeCharacterDto>> GetAnimeCharactersAsync(long malId, CancellationToken token)
        {
            var cached = Cache.Get<List<AnimeCharacterIdCacheDto>>(Keys.Characters(malId));

            if (cached is not null)
            {
                return await HydrateCharactersAsync(cached, token);
            }

            var index = await GetAnimeCharacterIndexAsync(malId, token);

            if (index is null)
                return new List<AnimeCharacterDto>();

            var expiry = GetExpiry(URLs.AnimeCharacters(malId));

            Cache.Put(Keys.Characters(malId), index, expiry);

            return await HydrateCharactersAsync(index, token);
        }
    }
}
