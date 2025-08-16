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

            JikanDotNet.Anime anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken);
            if (anime == null) return result;

            var (episodeNumber, seasonNumber, malId) = await GetSeasonEpisodeNumberAsync(
                info.IndexNumber.Value,
                info.ParentIndexNumber.GetValueOrDefault(),
                anime,
                cancellationToken
            ).ConfigureAwait(false);

            EpisodeSearchResult episodeResult = new EpisodeSearchResult();
            AnimeEpisode episodeData = null;

            try
            {
                episodeData = (await _jikan.GetAnimeEpisodeAsync(malId.Value, episodeNumber, cancellationToken).ConfigureAwait(false))?.Data;
            }
            catch (JikanRequestException)
            {
                // Expected for missing episode info
            }
            if (episodeData == null) return result;

            episodeResult.episode = episodeData;
            result.HasMetadata = true;
            result.Item = episodeResult.ToEpisode(anime.Episodes?.ToString().Length ?? 4);
            result.Item.IndexNumber = info.IndexNumber;
            result.Provider = ProviderNames.MyAnimeList;
            return result;
        }

        private async Task<(int episodeNumber, int seasonNumber, long? malId)> GetSeasonEpisodeNumberAsync(
            int episodeNumber,
            int seasonNumber,
            JikanDotNet.Anime anime,
            CancellationToken cancellationToken)
        {
            while (anime.Episodes.HasValue && anime.Episodes.Value > 0 && episodeNumber > anime.Episodes.Value)
            {
                episodeNumber -= anime.Episodes.Value;

                var sequel = (await _jikan.GetAnimeRelationsAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false))
                             ?.Data.FirstOrDefault(r => r.Relation.Equals("Sequel", StringComparison.OrdinalIgnoreCase))
                             ?.Entry.FirstOrDefault();

                if (sequel == null) break;

                seasonNumber++;
                var sequelAnime = (await _jikan.GetAnimeAsync(sequel.MalId, cancellationToken).ConfigureAwait(false))?.Data;
                if (sequelAnime == null || !sequelAnime.Episodes.HasValue || sequelAnime.Episodes.Value == 0) break;

                anime = sequelAnime;
            }

            return (episodeNumber, seasonNumber, anime.MalId);
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
