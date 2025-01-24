using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using JikanDotNet;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Series = MediaBrowser.Controller.Entities.TV.Series;
using SeriesInfo = MediaBrowser.Controller.Providers.SeriesInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        private readonly ILogger<MyAnimeListSeriesProvider> _log;
        private readonly Jikan _jikan;
        private MyAnimeListSearchHelper _searchHelper;
        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListSeriesProvider(ILogger<MyAnimeListSeriesProvider> logger)
        {
            _log = logger;
            _jikan = JikanSingleton.Instance;
            _searchHelper = new MyAnimeListSearchHelper(_jikan);
        }

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            MetadataResult<Series> result = new MetadataResult<Series>();
            long? malId = _searchHelper.GetAnimeIdAsync(_log, info, cancellationToken).Result;
            if (!malId.HasValue) return result;

            var media = await GetAnimeInfoAsync(malId.Value, cancellationToken);
            if (media.anime == null) return result;

            result.HasMetadata = true;
            result.Item = media.ToSeries();
            result.People = media.GetPeopleInfo();
            result.Provider = ProviderNames.MyAnimeList;
            return result;
        }

        private async Task<Anime> GetAnimeInfoAsync(long malId, CancellationToken cancellationToken)
        {
            _log.LogInformation("Fetching Series metadata for MAL ID: {aid}", malId);
            var media = new Anime
            {
                anime = (await _jikan.GetAnimeAsync(malId, cancellationToken).ConfigureAwait(false)).Data,
                characters = (await _jikan.GetAnimeCharactersAsync(malId, cancellationToken).ConfigureAwait(false)).Data
            };
            return media;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (info.Path == null || !info.IndexNumber.HasValue) return result;

            long? aid = _searchHelper.GetAnimeIdAsync(_log, info, cancellationToken).Result;
            if (aid.HasValue)
            {
                var searchResult = new AnimeSearchResult();
                searchResult.anime = (await _jikan.GetAnimeAsync(aid.Value, cancellationToken).ConfigureAwait(false)).Data;
                if (searchResult.anime != null)
                {
                    result.Add(searchResult.ToSearchResult());
                }
            }

            return result;
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = Plugin.Instance.GetHttpClient();
            return await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
    }
}
