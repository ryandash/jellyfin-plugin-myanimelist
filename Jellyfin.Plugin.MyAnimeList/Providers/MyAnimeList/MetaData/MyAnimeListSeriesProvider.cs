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
        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListSeriesProvider(ILogger<MyAnimeListSeriesProvider> logger)
        {
            _log = logger;
            _jikan = JikanSingleton.Instance;
        }

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            long? aid = MyAnimeListSearchHelper.GetAnimeIdAsync(info, cancellationToken).Result;
            MetadataResult<Series> result = new MetadataResult<Series>();
            if (!aid.HasValue) return result;
            Anime media = await GetAnimeInfoAsync(aid.Value, cancellationToken);
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
            Anime media = new Anime
            {
                anime = (await _jikan.GetAnimeAsync(malId, cancellationToken).ConfigureAwait(false)).Data,
                characters = (await _jikan.GetAnimeCharactersAsync(malId, cancellationToken).ConfigureAwait(false)).Data
            };
            return media;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            long? aid = MyAnimeListSearchHelper.GetAnimeIdAsync(info, cancellationToken).Result;

            if (aid.HasValue)
            {
                AnimeSearchResult result = new AnimeSearchResult();
                result.anime = (await _jikan.GetAnimeAsync(aid.Value, cancellationToken).ConfigureAwait(false)).Data;
                if (result.anime != null)
                {
                    results.Add(result.ToSearchResult());
                }
            }

            return results;
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = Plugin.Instance.GetHttpClient();
            return await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
    }
}
