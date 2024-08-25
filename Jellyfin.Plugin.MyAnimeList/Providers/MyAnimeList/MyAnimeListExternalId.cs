using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class MyAnimeListExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item)
            => item is Series || item is Movie;

        public string ProviderName
            => "MyAnimeList";

        public string Key
            => ProviderNames.MyAnimeList;

        public ExternalIdMediaType? Type
            => ExternalIdMediaType.Series;

        public string UrlFormatString
            => "https://MyAnimeList.net/anime/{0}/";
    }
}
