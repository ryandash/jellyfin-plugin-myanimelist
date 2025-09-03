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
        private readonly Jikan _jikan;
        private readonly MyAnimeListSearchHelper _searchHelper;

        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListEpisodeProvider(ILogger<MyAnimeListEpisodeProvider> logger)
        {
            _log = logger;
            _jikan = JikanSingleton.Instance;
            _searchHelper = new MyAnimeListSearchHelper(_jikan);
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
                anime.anime,
                cancellationToken
            ).ConfigureAwait(false);
            long malID = anime.anime.MalId.GetValueOrDefault();

            AnimeEpisode episodeData = null;
            try
            {
                if (anime.anime.Episodes != 1)
                {
                    episodeData = (await _jikan.GetAnimeEpisodeAsync(malID, episodeNumber, cancellationToken).ConfigureAwait(false))?.Data;
                } else
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
            JikanDotNet.Anime anime,
            CancellationToken cancellationToken)
        {
            while (anime.Episodes.HasValue && anime.Episodes.Value > 0 && episodeNumber > anime.Episodes.Value)
            {
                
                var sequel = (await _jikan.GetAnimeRelationsAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false))
                             ?.Data.FirstOrDefault(r => r.Relation.Equals("Sequel", StringComparison.OrdinalIgnoreCase))
                             ?.Entry.FirstOrDefault();
                if (sequel == null) break;
                var sequelAnime = (await _jikan.GetAnimeAsync(sequel.MalId, cancellationToken).ConfigureAwait(false))?.Data;
                if (sequelAnime == null || !sequelAnime.Episodes.HasValue || sequelAnime.Episodes.Value == 0) break;

                episodeNumber -= anime.Episodes.Value;
                anime = sequelAnime;
            }

            if (episodeNumber == 0)
            {
                var prequel = (await _jikan.GetAnimeRelationsAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false))
                             ?.Data.FirstOrDefault(r => r.Relation.Equals("Prequel", StringComparison.OrdinalIgnoreCase))
                             ?.Entry.FirstOrDefault();
                if (prequel != null)
                {
                    var prequelAnime = (await _jikan.GetAnimeAsync(prequel.MalId, cancellationToken).ConfigureAwait(false))?.Data;
                    if (prequelAnime != null && prequelAnime.Episodes.HasValue && prequelAnime.Episodes.Value != 0)
                    {
                        anime = prequelAnime;
                        episodeNumber += anime.Episodes.Value;
                    }
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
