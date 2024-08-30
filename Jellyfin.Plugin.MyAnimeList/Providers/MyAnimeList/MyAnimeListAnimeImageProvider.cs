using JikanDotNet;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class MyAnimeListAnimeImageProvider : IRemoteImageProvider
    {
        private readonly Jikan _jikan;
        public MyAnimeListAnimeImageProvider()
        {
            _jikan = NewJikan._jikan;
        }

        public string Name => "MyAnimeList";

        public bool Supports(BaseItem item) => item is Series || item is MediaBrowser.Controller.Entities.TV.Season || item is Movie;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var straid = item.GetProviderId(ProviderNames.MyAnimeList);
            var list = new List<RemoteImageInfo>();

            if (!string.IsNullOrEmpty(straid))
            {
                long aid = long.Parse(straid);
                Media media = new Media();
                media.anime = (await _jikan.GetAnimeAsync(aid, cancellationToken)).Data;
                ICollection<ImagesSet> pictures = (await _jikan.GetAnimePicturesAsync(aid, cancellationToken)).Data;
                if (media != null)
                {
                    if (media.GetImageUrl() != null)
                    {
                        list.Add(new RemoteImageInfo
                        {
                            ProviderName = Name,
                            Type = ImageType.Primary,
                            Url = media.GetImageUrl()
                        });
                    }
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
