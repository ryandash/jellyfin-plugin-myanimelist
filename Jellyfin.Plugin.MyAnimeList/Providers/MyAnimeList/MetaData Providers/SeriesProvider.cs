using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using SeriesInfo = MediaBrowser.Controller.Providers.SeriesInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class SeriesProvider : BaseProvider<Series, SeriesInfo>
    {
        public SeriesProvider(ILogger<SeriesProvider> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory) : base(logger, libraryManager, httpClientFactory)
        {
        }

        protected override Series ConvertToItem(AnimeObject media, SeriesInfo info) => media.ToSeries(info);
    }
}
