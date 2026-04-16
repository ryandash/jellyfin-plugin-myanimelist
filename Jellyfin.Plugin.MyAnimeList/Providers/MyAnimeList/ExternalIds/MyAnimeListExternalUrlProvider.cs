using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.ExternalIds
{
    public class MyAnimeListExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "MyAnimeList";

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (item.TryGetProviderId(ProviderNames.MyAnimeList, out var externalId))
            {
                switch (item)
                {
                    case Series:
                    case Movie:
                    case Season:
                        yield return $"https://myanimelist.net/anime/{externalId}/";
                        break;
                    case Person:
                        yield return $"https://myanimelist.net/people/{externalId}/";
                        break;
                    case Episode:
                        yield return externalId;
                        break;
                }
            }
        }
    }
}
