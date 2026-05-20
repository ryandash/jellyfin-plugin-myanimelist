using System.Net.Http;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using Season = MediaBrowser.Controller.Entities.TV.Season;
using SeasonInfo = MediaBrowser.Controller.Providers.SeasonInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListSeasonProvider : MyAnimeListBaseProvider<Season, SeasonInfo>
    {
        public MyAnimeListSeasonProvider(ILogger<MyAnimeListSeasonProvider> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory) : base(logger, libraryManager, httpClientFactory)
        {
        }

        protected override Season ConvertToItem(Season existing, AnimeObject media, ItemLookupInfo info)
            => media.ToSeason(existing, info as SeasonInfo);
    }
}
