using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.ExternalIds
{
    public class MyAnimeListAnimeEpisodeExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item) =>
            item is Episode;

        public string ProviderName => ProviderNames.MyAnimeListSeason;

        public string Key => ProviderNames.MyAnimeListSeason;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Season;

        public string UrlFormatString => "https://myanimelist.net/anime/{0}/";
    }
}
