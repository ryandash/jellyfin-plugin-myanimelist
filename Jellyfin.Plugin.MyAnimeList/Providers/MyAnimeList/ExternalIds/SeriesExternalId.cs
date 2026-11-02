using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.ExternalIds
{
    public class SeriesExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item) => item is Series;

        public string ProviderName => ProviderNames.MyAnimeList;

        public string Key => ProviderNames.MyAnimeList;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Series;
    }
}
