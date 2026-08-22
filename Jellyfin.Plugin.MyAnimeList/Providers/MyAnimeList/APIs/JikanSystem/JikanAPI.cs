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

        private static bool IsTenrai(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            return string.Equals(
                uri.Host,
                defaultPrimaryBaseUrl,
                StringComparison.OrdinalIgnoreCase);
        }

        private static Jikan CreateClient(string baseUrl)
        {
            var http = new HttpClient(new JikanHeaderHandler(new HttpClientHandler()))
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromMinutes(5)
            };

            JikanClientConfiguration config = new JikanClientConfiguration();

            if (IsTenrai(baseUrl))
            {
                config.LimiterConfigurations = new List<TaskLimiterConfiguration>
                {
                    new TaskLimiterConfiguration(1, TimeSpan.FromMilliseconds(250)),
                    new TaskLimiterConfiguration(4, TimeSpan.FromSeconds(1)),
                    new TaskLimiterConfiguration(120, TimeSpan.FromMinutes(1))
                };
            }

            return new Jikan(config, http);
        }

        private static readonly object ClientLock = new();

        private static void EnsureClients()
        {
            var p = string.IsNullOrWhiteSpace(_config.PrimaryJikanUrl)
                ? defaultPrimary
                : _config.PrimaryJikanUrl;

            var b = string.IsNullOrWhiteSpace(_config.BackupJikanUrl)
                ? defaultBackup
                : _config.BackupJikanUrl;

            if (_primaryClient != null && _backupClient != null && p == _primaryUrl && b == _backupUrl)
            {
                return;
            }

            lock (ClientLock)
            {
                p = string.IsNullOrWhiteSpace(_config.PrimaryJikanUrl)
                    ? defaultPrimary
                    : _config.PrimaryJikanUrl;

                b = string.IsNullOrWhiteSpace(_config.BackupJikanUrl)
                    ? defaultBackup
                    : _config.BackupJikanUrl;

                if (_primaryClient == null || p != _primaryUrl)
                {
                    _primaryClient = CreateClient(p);
                    _primaryUrl = p;
                }

                if (_backupClient == null || b != _backupUrl)
                {
                    _backupClient = CreateClient(b);
                    _backupUrl = b;
                }
            }
        }

        private static async Task<JikanResult<T>> TryPrimaryThenBackup<T>(Func<Jikan, Task<T>> action, CancellationToken token)
        {
            EnsureClients();

            try
            {
                return new JikanResult<T>
                {
                    Data = await action(_primaryClient).ConfigureAwait(false),
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
                        Data = await action(_backupClient).ConfigureAwait(false),
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

                fetch: () => TryPrimaryThenBackup(j => j.GetAnimeFullDataAsync(malId, token), token),

                normalize: a => AnimeFullCacheDto.From(a.Data.Data),

                ignoreCache: cached is not null
            );
        }

        public static async Task<EpisodeCacheDto> GetAnimeEpisodeAsync(long malId, int episodeNumber, CancellationToken token)
        {
            return await GetMergedAsync(
                Keys.AnimeEpisode(malId, episodeNumber),
                URLs.AnimeSpecificEpisodes(malId, episodeNumber),

                fetch: () => TryPrimaryThenBackup(j => j.GetAnimeEpisodeAsync(malId, episodeNumber, token), token),

                normalize: e => EpisodeCacheDto.From(e.Data.Data),

                merge: EpisodeCacheDto.MergeEpisodeDetails,

                isComplete: e => e.HasFullDetails
            ).ConfigureAwait(false);
        }

        public static async Task<PersonDto> GetPersonAsync(long malId, CancellationToken token)
        {
            return await GetMergedAsync(
                Keys.Person(malId),
                URLs.Person(malId),

                fetch: () => TryPrimaryThenBackup(j => j.GetPersonAsync(malId, token), token),

                normalize: p => PersonDto.From(p.Data.Data),

                merge: PersonDto.MergePersonDetails,

                isComplete: p => p.HasFullDetails
            ).ConfigureAwait(false);
        }

        public static async Task<CharacterCacheDto> GetCharacterAsync(long malId, CancellationToken token)
        {
            return await GetMergedAsync(
                Keys.Character(malId),
                URLs.Character(malId),

                fetch: () => TryPrimaryThenBackup(j => j.GetCharacterAsync(malId, token), token),

                normalize: c => CharacterCacheDto.From(c.Data.Data),

                merge: CharacterCacheDto.MergeCharacterDetails,

                isComplete: c => c.HasFullDetails
            ).ConfigureAwait(false);
        }

        // SEARCH METHODS
        public static async Task<List<AnimeFullCacheDto>> SearchAnimeAsync(string query, bool nsfw, bool isMovie, CancellationToken token)
        {
            var key = Keys.SearchAnime(query, nsfw);

            var cached = Cache.Get<List<long>>(key);
            if (cached is not null)
            {
                var results = new List<AnimeFullCacheDto>();

                foreach (var id in cached)
                {
                    var result = await GetAnimeFullAsync(id, token, false).ConfigureAwait(false);

                    if (result is not null)
                        results.Add(result);
                }

                return results;
            }

            AnimeSearchConfig searchConfig = new AnimeSearchConfig
            {
                Query = query,
                Page = 1,
                Sfw = !nsfw,
                Type = isMovie ? AnimeType.Movie : AnimeType.EveryType,
            };
            var search = await TryPrimaryThenBackup(j => j.SearchAnimeAsync(searchConfig, token), token);
            var ids = search.Data.Data.Where(a => a.MalId.HasValue).Select(a => (anime: a, id: a.MalId.Value)).ToList();
            var anime = new List<AnimeFullCacheDto>();

            foreach (var a in ids)
            {
                var animeCache = AnimeFullCacheDto.From(a.anime);

                if (animeCache is not null)
                {
                    Cache.Put(Keys.AnimeFull(a.id), animeCache, GetExpiry(URLs.AnimeFull(a.id)));

                    anime.Add(animeCache);
                }
            }

            Cache.Put(key, ids.Select(a => a.id).ToList(), DateTime.UtcNow.Add(SearchExpiry));

            return anime.ToList();
        }

        public static async Task<List<CharacterCacheDto>> SearchCharacterAsync(string term, CancellationToken token)
        {
            var key = Keys.SearchCharacter(term);

            var cached = Cache.Get<List<long>>(key);
            if (cached is not null)
            {
                var results = new List<CharacterCacheDto>();

                foreach (var id in cached)
                {
                    var result = await GetCharacterAsync(id, token).ConfigureAwait(false);

                    if (result is not null)
                        results.Add(result);
                }

                return results;
            }

            CharacterSearchConfig searchConfig = new CharacterSearchConfig
            {
                Query = term,
                Page = 1
            };
            var search = await TryPrimaryThenBackup(j => j.SearchCharacterAsync(searchConfig, token), token);
            var ids = search.Data.Data.Select(a => (character: a, id: a.MalId)).ToList();
            var characters = new List<CharacterCacheDto>();
            foreach (var a in ids)
            {
                var character = CharacterCacheDto.From(a.character);

                if (character is not null)
                {
                    Cache.Put(Keys.Character(a.id), character, GetExpiry(URLs.Character(a.id)));

                    characters.Add(character);
                }
            }

            Cache.Put(key, ids.Select(a => a.id).ToList(), DateTime.UtcNow.Add(SearchExpiry));

            return characters.ToList();
        }

        public static async Task<List<PersonDto>> SearchPersonAsync(string term, CancellationToken token)
        {
            var key = Keys.SearchPerson(term);
            var cached = Cache.Get<List<long>>(key);
            if (cached is not null)
            {
                var results = new List<PersonDto>();

                foreach (var id in cached)
                {
                    var result = await GetPersonAsync(id, token).ConfigureAwait(false);

                    if (result is not null)
                        results.Add(result);
                }

                return results;
            }

            PersonSearchConfig searchConfig = new PersonSearchConfig
            {
                Query = term,
                Page = 1
            };
            var search = await TryPrimaryThenBackup(j => j.SearchPersonAsync(searchConfig, token), token);
            var ids = search.Data.Data.Select(a => (person: a, id: a.MalId)).ToList();
            var people = new List<PersonDto>();
            foreach (var a in ids)
            {
                var person = PersonDto.From(a.person);

                if (person is not null)
                {
                    Cache.Put(Keys.Person(a.id), person, GetExpiry(URLs.Person(a.id)));

                    people.Add(person);
                }
            }
            Cache.Put(key, ids.Select(a => a.id).ToList(), DateTime.UtcNow.Add(SearchExpiry));

            return people.ToList();
        }

        // MORE ADVANCED GET METHODS
        public static async Task<List<EpisodeCacheDto>> GetAnimeEpisodesAsync(long malId, CancellationToken token)
        {
            return await GetOrFetchAsync(
                Keys.AnimeEpisodes(malId),
                URLs.AnimeEpisodes(malId),

                fetch: async () =>
                {
                    var first = (await TryPrimaryThenBackup(
                        j => j.GetAnimeEpisodesAsync(malId, 1, token),
                        token)).Data;

                    if (first?.Data == null || first.Data.Count == 0)
                        return new List<AnimeEpisode>();

                    var allEpisodes = new List<AnimeEpisode>(first.Data);

                    if (first.Pagination?.HasNextPage != true)
                        return allEpisodes;

                    var lastPage = first.Pagination.LastVisiblePage;

                    for (var page = 2; page <= lastPage; page++)
                    {
                        var result = await TryPrimaryThenBackup(
                            j => j.GetAnimeEpisodesAsync(malId, page, token),
                            token
                        ).ConfigureAwait(false);

                        if (result.Data?.Data != null)
                            allEpisodes.AddRange(result.Data.Data);
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
            ).ConfigureAwait(false);
        }

        private static async Task<List<AnimeCharacterIdCacheDto>> GetAnimeCharacterIndexAsync(long malId, CancellationToken token)
        {
            return await GetOrFetchAsync(
                Keys.Characters(malId),
                URLs.AnimeCharacters(malId),

                fetch: () => TryPrimaryThenBackup(j => j.GetAnimeCharactersAsync(malId, token), token),

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

                    return res.Data.Data.Select(c => AnimeCharacterIdCacheDto.From(c)).Where(x => x is not null).ToList();
                }
            ).ConfigureAwait(false);
        }

        private static async Task<List<AnimeCharacterDto>> HydrateCharactersAsync(List<AnimeCharacterIdCacheDto> cached, CancellationToken token)
        {
            var results = new List<AnimeCharacterDto>();

            foreach (var r in cached)
            {
                token.ThrowIfCancellationRequested();

                var charID = r.CharacterId;

                var character = await GetOrFetchAsync(
                    Keys.Character(charID),
                    URLs.Character(charID),

                    fetch: () => TryPrimaryThenBackup(j => j.GetCharacterAsync(charID, token), token),

                    normalize: res => CharacterCacheDto.From(res.Data.Data)
                ).ConfigureAwait(false);

                if (character is null)
                    continue;

                var voiceActors = new List<VoiceActorEntryDto>();

                foreach (var va in r.VoiceActors)
                {
                    token.ThrowIfCancellationRequested();

                    var person = await GetOrFetchAsync(
                        Keys.Person(va.Person.MalId),
                        URLs.Person(va.Person.MalId),

                        fetch: () => TryPrimaryThenBackup(j => j.GetPersonAsync(va.Person.MalId, token), token),

                        normalize: res => PersonDto.From(res.Data.Data)
                    ).ConfigureAwait(false);

                    if (person is not null)
                    {
                        voiceActors.Add(new VoiceActorEntryDto
                        {
                            Language = va.Language,
                            Person = person
                        });
                    }
                }

                results.Add(new AnimeCharacterDto
                {
                    Character = character,
                    Role = r.Role,
                    VoiceActors = voiceActors
                });
            }

            return results;
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
