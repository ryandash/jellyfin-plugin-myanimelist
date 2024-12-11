using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class MyAnimeListAnimeEpisodeExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item) =>
            item is Episode;

        public string ProviderName => ProviderNames.MyAnimeList;

        public string Key => ProviderNames.MyAnimeListEP;

        public ExternalIdMediaType? Type => null;

        public string UrlFormatString => "{0}";
    }
}
