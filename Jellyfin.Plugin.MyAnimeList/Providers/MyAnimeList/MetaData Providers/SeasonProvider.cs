using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using SeasonInfo = MediaBrowser.Controller.Providers.SeasonInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class SeasonProvider : BaseProvider<MediaBrowser.Controller.Entities.TV.Season, SeasonInfo>
    {
        public SeasonProvider(ILogger<SeasonProvider> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory) : base(logger, libraryManager, httpClientFactory)
        {
        }

        protected override MediaBrowser.Controller.Entities.TV.Season ConvertToItem(AnimeObject media, ItemLookupInfo info)
            => media.ToSeason(info as SeasonInfo);
    }
}
