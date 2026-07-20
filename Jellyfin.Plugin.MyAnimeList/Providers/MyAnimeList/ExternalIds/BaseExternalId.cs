using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.ExternalIds
{
    public class BaseExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item) =>
            item is Series || item is Movie || item is Season;

        public string ProviderName => ProviderNames.MyAnimeList;

        public string Key => ProviderNames.MyAnimeList;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Series;
    }
}
