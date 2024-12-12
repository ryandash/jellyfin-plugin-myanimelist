using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.ExternalIds
{
    public class MyAnimeListAnimeExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item) =>
            item is Series || item is Movie || item is Season;

        public string ProviderName => ProviderNames.MyAnimeList;

        public string Key => ProviderNames.MyAnimeList;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Series;

        public string UrlFormatString => "https://myanimelist.net/anime/{0}/";
    }
}
