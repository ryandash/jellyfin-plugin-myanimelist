using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Season = MediaBrowser.Controller.Entities.TV.Season;
using SeasonInfo = MediaBrowser.Controller.Providers.SeasonInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListSeasonProvider : MyAnimeListBaseProvider<Season, SeasonInfo>
    {
        public MyAnimeListSeasonProvider(ILogger<MyAnimeListSeasonProvider> logger, ILibraryManager libraryManager) : base(logger, libraryManager)
        {

        }

        protected override Season ConvertToItem(Anime media)
            => media.ToSeason();
    }
}
