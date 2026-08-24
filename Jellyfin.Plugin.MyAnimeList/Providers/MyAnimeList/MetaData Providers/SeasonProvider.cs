using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using SeasonInfo = MediaBrowser.Controller.Providers.SeasonInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class SeasonProvider : BaseProvider<Season, SeasonInfo>
    {
        public SeasonProvider(ILogger<SeasonProvider> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory) : base(logger, libraryManager, httpClientFactory)
        {
        }

        protected override Season ConvertToItem(AnimeObject media, SeasonInfo info) => media.ToSeason(info);
    }
}
