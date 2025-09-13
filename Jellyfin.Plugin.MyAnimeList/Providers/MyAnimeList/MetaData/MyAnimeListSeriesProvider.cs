using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
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
        private readonly MyAnimeListSearchHelper _searchHelper;
        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListSeriesProvider(ILogger<MyAnimeListSeriesProvider> logger)
        {
            _log = logger;
            _searchHelper = new MyAnimeListSearchHelper();
        }

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            MetadataResult<Series> result = new MetadataResult<Series>();
            JikanDotNet.Anime anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken);
            if (anime == null) return result;

            Anime media = new Anime
            {
                anime = anime,
                characters = (await JikanSingleton.GetAnimeCharactersAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false))?.Data
            };

            result.HasMetadata = true;
            result.Item = media.ToSeries();
            result.People = media.GetPeopleInfo();
            result.Provider = ProviderNames.MyAnimeList;
            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            if (info.Path == null || !info.IndexNumber.HasValue) return result;

            JikanDotNet.Anime anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken);
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
