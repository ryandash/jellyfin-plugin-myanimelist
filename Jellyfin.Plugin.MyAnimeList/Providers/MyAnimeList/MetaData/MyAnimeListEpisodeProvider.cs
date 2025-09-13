using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using JikanDotNet;
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
            MetadataResult<Episode> result = new MetadataResult<Episode>();
            if (info.Path == null || !info.IndexNumber.HasValue) return result;
            var anime = new Anime
            {
                anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken)
            };
            if (anime == null) return result;

            (var episodeNumber, anime.anime) = await GetSeasonEpisodeNumberAsync(
                info.IndexNumber.Value,
                info.ParentIndexNumber.Value,
                anime.anime,
                cancellationToken
            ).ConfigureAwait(false);

            if (anime.anime == null) return result;
            long malID = anime.anime.MalId.GetValueOrDefault();

            AnimeEpisode episodeData = null;
            try
            {
                if (anime.anime.Episodes != 1)
                {
                    episodeData = (await JikanSingleton.GetAnimeEpisodeAsync(malID, episodeNumber, cancellationToken).ConfigureAwait(false))?.Data;
                }
                else
                {
                    episodeData = anime.toEpisodeData();
                }
            }
            catch (JikanRequestException)
            {
                _log.LogInformation($"No episode data for {malID} {episodeNumber}");
                // Expected for missing episode info
            }
            if (episodeData == null) return result;

            EpisodeSearchResult episodeResult = new EpisodeSearchResult();
            episodeResult.episode = episodeData;
            result.HasMetadata = true;
            result.Item = episodeResult.ToEpisode(anime.anime.Episodes?.ToString().Length ?? 4);
            result.Item.IndexNumber = info.IndexNumber;
            result.Provider = ProviderNames.MyAnimeList;
            return result;
        }

        private async Task<(int episodeNumber, JikanDotNet.Anime anime)> GetSeasonEpisodeNumberAsync(
    int episodeNumber,
    int seasonNumber,
    JikanDotNet.Anime anime,
    CancellationToken cancellationToken)
        {
            List<RelatedEntry> relations = null;

            async Task<JikanDotNet.Anime> GetRelatedAnimeAsync(string relationType)
            {
                relations ??= (await JikanSingleton.GetAnimeRelationsAsync(anime.MalId!.Value, cancellationToken)
                    .ConfigureAwait(false))?.Data?.ToList() ?? new List<RelatedEntry>();

                var relation = relations.FirstOrDefault(r =>
                    r.Relation.Equals(relationType, StringComparison.OrdinalIgnoreCase))
                    ?.Entry.FirstOrDefault();

                return relation == null
                    ? null
                    : (await JikanSingleton.GetAnimeAsync(relation.MalId, cancellationToken).ConfigureAwait(false))?.Data;
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
                    .ConfigureAwait(false))?.Data?.ToList() ?? new List<RelatedEntry>();

                var sideStories = relations.FirstOrDefault(r =>
                    r.Relation.Equals("Side Story", StringComparison.OrdinalIgnoreCase))?.Entry;

                if (sideStories != null)
                {
                    var tasks = sideStories.Select(s => JikanSingleton.GetAnimeAsync(s.MalId, cancellationToken));
                    var allResults = await Task.WhenAll(tasks);
                    var allAnimes = allResults.Select(r => r?.Data).OfType<JikanDotNet.Anime>();

                    var movies = new List<JikanDotNet.Anime>();
                    var others = new List<JikanDotNet.Anime>();

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
