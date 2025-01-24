using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using JikanDotNet;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Season = MediaBrowser.Controller.Entities.TV.Season;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListAnimeImageProvider : IRemoteImageProvider
    {
        private readonly Jikan _jikan;
        private readonly ILogger _log;
        public MyAnimeListAnimeImageProvider(ILogger<MyAnimeListAnimeImageProvider> logger)
        {
            _log = logger;
            _jikan = JikanSingleton.Instance;
        }

        public string Name => "MyAnimeList";

        public bool Supports(BaseItem item) => item is Series || item is Season || item is Movie;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return [ImageType.Primary];
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            var malId = item.GetProviderId(ProviderNames.MyAnimeList);
            if (string.IsNullOrEmpty(malId))
            {
                return list;
            }

            long aid = long.Parse(malId);
            _log.LogInformation("Populating Images metadata for: {malId}", aid);
            var media = new Anime();
            media.anime = (await _jikan.GetAnimeAsync(aid, cancellationToken))?.Data;
            if (media.anime != null)
            {
                var images = await _jikan.GetAnimePicturesAsync(aid, cancellationToken);
                if (images?.Data != null && media.GetImageUrl() != null)
                {
                    list.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Type = ImageType.Primary,
                        Url = media.GetImageUrl()
                    });
                }
            }

            return list;
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = Plugin.Instance.GetHttpClient();

            return await httpClient.GetAsync(url).ConfigureAwait(false);
        }
    }
}
