using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.ExternalIds
{
    public class BookExternalId : IExternalId
    {
        public bool Supports(IHasProviderIds item) => item is Book;

        public string ProviderName => ProviderNames.MyAnimeList;

        public string Key => ProviderNames.MyAnimeList;

        public ExternalIdMediaType? Type => ExternalIdMediaType.Book;
    }
}
