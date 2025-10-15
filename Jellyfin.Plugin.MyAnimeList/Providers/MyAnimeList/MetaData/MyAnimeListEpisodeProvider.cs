using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using JikanDotNet.Exceptions;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using EpisodeInfo = MediaBrowser.Controller.Providers.EpisodeInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
    {
        private readonly ILogger<MyAnimeListEpisodeProvider> _log;
        private readonly MyAnimeListSearchHelper _searchHelper;

        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListEpisodeProvider(ILogger<MyAnimeListEpisodeProvider> logger)
        {
            _log = logger;
            _searchHelper = new MyAnimeListSearchHelper();
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>();

            if (info.Path == null || !info.IndexNumber.HasValue)
                return result;

            var config = Plugin.Instance.Configuration;
            var enableDebug = config.EnableDebug;

            EpisodeCacheDto episodeData = null;
            Anime anime = null;

            if (config.UseExternalIDs && info is EpisodeInfo episodeInfo && episodeInfo.ProviderIds.TryGetValue("Tvdb", out var tvdbid))
            {
                if (enableDebug) _log.LogInformation("Found TVDB ID {TvdbId}", tvdbid);

                var idMapping = new IdMappings();
                var epResult = await idMapping.GetAnimeEpisodeMappingAsync(tvdbid).ConfigureAwait(false);

                if (epResult?.MalId is long malId)
                {
                    if (enableDebug) _log.LogInformation("MalID: {MalId} Season: {Season} Episode: {Episode}",
                        malId, epResult.Season, epResult.Episode);

                    anime = new Anime
                    {
                        anime = await _searchHelper
                            .GetCurrentAnimeSeasonAsync(malId, info.ParentIndexNumber ?? 1, cancellationToken)
                            .ConfigureAwait(false)
                    };

                    episodeData = await JikanSingleton
                        .GetAnimeEpisodeAsync(malId, epResult.Episode!.Value, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (episodeData == null)
            {
                anime = new Anime
                {
                    anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken).ConfigureAwait(false)
                };

                if (anime?.anime == null)
                    return result;

                var (episodeNumber, updatedAnime) = await GetSeasonEpisodeNumberAsync(
                    info.IndexNumber.Value,
                    info.ParentIndexNumber!.Value,
                    anime.anime,
                    cancellationToken
                ).ConfigureAwait(false);

                anime.anime = updatedAnime;

                if (anime.anime == null)
                    return result;

                var malId = anime.anime.MalId.GetValueOrDefault();

                try
                {
                    episodeData = anime.anime.Episodes != 1
                        ? await JikanSingleton.GetAnimeEpisodeAsync(malId, episodeNumber, cancellationToken).ConfigureAwait(false)
                        : anime.toEpisodeData();
                }
                catch (JikanRequestException)
                {
                    _log.LogInformation("No episode data for MAL ID {MalId}, episode {EpisodeNumber}", malId, episodeNumber);
                }
            }

            if (episodeData == null)
                return result;

            var episodeResult = new EpisodeSearchResult { episode = episodeData };

            result.HasMetadata = true;
            result.Item = episodeResult.ToEpisode(anime!.anime?.Episodes?.ToString().Length ?? 4);
            result.Item.IndexNumber = info.IndexNumber;
            result.Provider = ProviderNames.MyAnimeList;

            return result;
        }

        private async Task<(int episodeNumber, AnimeCacheDto anime)> GetSeasonEpisodeNumberAsync(
    int episodeNumber,
    int seasonNumber,
    AnimeCacheDto anime,
    CancellationToken cancellationToken)
        {
            List<RelatedEntryDto> relations = null;

            async Task<AnimeCacheDto> GetRelatedAnimeAsync(string relationType)
            {
                relations = (await JikanSingleton.GetAnimeRelationsAsync(anime.MalId.Value, cancellationToken)
                    .ConfigureAwait(false));

                var relation = relations.FirstOrDefault(r =>
                    r.Relation.Equals(relationType, StringComparison.OrdinalIgnoreCase))
                    ?.Entry.FirstOrDefault();

                return relation.HasValue
                    ? await JikanSingleton.GetAnimeAsync(relation.Value, cancellationToken).ConfigureAwait(false)
                    : null;
            }


            while (anime.Episodes.HasValue && anime.Episodes.Value > 0 && episodeNumber > anime.Episodes.Value)
            {
                var sequelAnime = await GetRelatedAnimeAsync("Sequel");
                if (sequelAnime == null || !sequelAnime.Episodes.HasValue || sequelAnime.Episodes.Value == 0)
                    break;

                episodeNumber -= anime.Episodes.Value;
                anime = sequelAnime;
            }

            if (episodeNumber == 0)
            {
                var prequelAnime = await GetRelatedAnimeAsync("Prequel");
                if (prequelAnime != null && prequelAnime.Episodes.HasValue && prequelAnime.Episodes.Value != 0)
                {
                    anime = prequelAnime;
                    episodeNumber += anime.Episodes.Value;
                }
                return (episodeNumber, anime);
            }

            if (seasonNumber == 0)
            {
                var config = Plugin.Instance.Configuration;
                if (config.ExcludeSpecials) return (0, null);

                relations ??= (await JikanSingleton.GetAnimeRelationsAsync(anime.MalId!.Value, cancellationToken)
                    .ConfigureAwait(false)).ToList() ?? new List<RelatedEntryDto>();

                var sideStories = relations.FirstOrDefault(r =>
                    r.Relation.Equals("Side Story", StringComparison.OrdinalIgnoreCase))?.Entry;

                if (sideStories != null)
                {
                    var tasks = sideStories.Select(s => JikanSingleton.GetAnimeAsync(s, cancellationToken));
                    var allResults = await Task.WhenAll(tasks);
                    var allAnimes = allResults.Select(r => r).OfType<AnimeCacheDto>();

                    var movies = new List<AnimeCacheDto>();
                    var others = new List<AnimeCacheDto>();

                    foreach (var a in allAnimes)
                    {
                        if (string.Equals(a.Type, "Movie", StringComparison.OrdinalIgnoreCase))
                            movies.Add(a);
                        else
                            others.Add(a);
                    }

                    if (episodeNumber > 0)
                    {
                        int targetIndex = episodeNumber - 1;
                        var orderedCount = movies.Count + others.Count;
                        if (targetIndex < orderedCount)
                        {
                            var targetAnime = targetIndex < movies.Count
                                ? movies[targetIndex]
                                : others[targetIndex - movies.Count];
                            return (1, targetAnime);
                        }
                    }

                    return (0, null);
                }
            }

            return (episodeNumber, anime);
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (info.Path == null || !info.IndexNumber.HasValue) return result;

            var anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken);
            if (anime == null) return result;

            var searchResult = new AnimeSearchResult();
            searchResult.anime = anime;
            result.Add(searchResult.ToSearchResult());

            return result;
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = Plugin.Instance.GetHttpClient();
            return await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
    }
}
