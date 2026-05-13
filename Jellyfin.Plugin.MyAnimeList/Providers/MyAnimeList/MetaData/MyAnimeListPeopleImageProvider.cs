using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Person = MediaBrowser.Controller.Entities.Person;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListPeopleImageProvider : IRemoteImageProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public string Name => ProviderNames.MyAnimeList;

        public bool Supports(BaseItem item) => item is Person;

        public MyAnimeListPeopleImageProvider(IHttpClientFactory httpClientFactory)
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

            var person = await JikanAPI.getPersonAsync(aid, cancellationToken).ConfigureAwait(false);

            var mainImageUrl = ImagesSetDto.GetImageUrl(person.Images.JPG);
            if (string.IsNullOrEmpty(mainImageUrl))
                return Array.Empty<RemoteImageInfo>();

            return new[]
            {
                new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Primary,
                    Url = mainImageUrl
                }
            };
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient(ProviderNames.MyAnimeList);
            return client.GetAsync(url, cancellationToken);
        }
    }
}
