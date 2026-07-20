using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class PersonExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item) =>
            item is Person;

        public string ProviderName => ProviderNames.MyAnimeList;

        public string Key => ProviderNames.MyAnimeList;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Person;
    }
}
