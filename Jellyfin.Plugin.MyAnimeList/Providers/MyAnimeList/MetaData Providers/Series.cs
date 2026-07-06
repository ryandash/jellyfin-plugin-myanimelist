using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using SeriesInfo = MediaBrowser.Controller.Providers.SeriesInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class Series : Base<MediaBrowser.Controller.Entities.TV.Series, SeriesInfo>
    {
        public Series(ILogger<Series> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory) : base(logger, libraryManager, httpClientFactory)
        {
        }

        protected override MediaBrowser.Controller.Entities.TV.Series ConvertToItem(AnimeObject media, ItemLookupInfo info)
            => media.ToSeries(info as SeriesInfo);
    }
}
