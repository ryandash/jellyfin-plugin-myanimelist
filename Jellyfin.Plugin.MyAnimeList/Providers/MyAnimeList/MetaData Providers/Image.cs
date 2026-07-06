using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class Image : IRemoteImageProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public string Name => ProviderNames.MyAnimeList;

        public bool Supports(BaseItem item) => item is MediaBrowser.Controller.Entities.TV.Series || item is MediaBrowser.Controller.Entities.TV.Season || item is MediaBrowser.Controller.Entities.Movies.Movie || item is Person;

        public Image(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return [ImageType.Primary];
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var malId = item.GetProviderId(ProviderNames.MyAnimeList);

            if (string.IsNullOrEmpty(malId))
                return Array.Empty<RemoteImageInfo>();


            if (!long.TryParse(malId, out long aid))
                return Array.Empty<RemoteImageInfo>();

            var images = new List<RemoteImageInfo>();
            string mainImageUrl;
            if (item is Person)
            {
                var person = await JikanAPI.getPersonAsync(aid, cancellationToken).ConfigureAwait(false);
                mainImageUrl = person.Images.Image;
            }
            else
            {
                var anime = await JikanAPI.GetAnimeFullAsync(aid, cancellationToken).ConfigureAwait(false);
                mainImageUrl = anime.Images.Image;
            }

            if (string.IsNullOrEmpty(mainImageUrl))
                return Array.Empty<RemoteImageInfo>();

            return
            [
                new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Primary,
                    Url = mainImageUrl
                }
            ];
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient(ProviderNames.MyAnimeList);
            return client.GetAsync(url, cancellationToken);
        }
    }
}
