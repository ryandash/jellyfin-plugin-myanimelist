using System.Net.Http;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using Series = MediaBrowser.Controller.Entities.TV.Series;
using SeriesInfo = MediaBrowser.Controller.Providers.SeriesInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListSeriesProvider : MyAnimeListBaseProvider<Series, SeriesInfo>
    {
        public MyAnimeListSeriesProvider(ILogger<MyAnimeListSeriesProvider> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory) : base(logger, libraryManager, httpClientFactory)
        {
        }

        protected override Series ConvertToItem(AnimeObject media, ItemLookupInfo info)
            => media.ToSeries(info as SeriesInfo);
    }
}
