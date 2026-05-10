using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using Season = MediaBrowser.Controller.Entities.TV.Season;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListAnimeImageProvider : IRemoteImageProvider
    {
        private static HttpClient _httpClient;

        public MyAnimeListAnimeImageProvider(ILogger<MyAnimeListAnimeImageProvider> logger)
        {
            _httpClient = Plugin.Instance?.GetHttpClient() ?? new HttpClient();
        }

        public string Name => "MyAnimeList";

        public bool Supports(BaseItem item) => item is Series || item is Season || item is Movie || item is Episode || item is Person;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return [ImageType.Primary];
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var malId = item.GetProviderId(ProviderNames.MyAnimeList);

            if (string.IsNullOrEmpty(malId))
                return Array.Empty<RemoteImageInfo>();

            long aid;

            if (item is Episode episode)
            {
                // MAL has not support regular episode images
                if (episode.ParentIndexNumber != 0)
                {
                    return Array.Empty<RemoteImageInfo>();
                }

                int start = malId.IndexOf("/anime/", StringComparison.Ordinal);
                if (start < 0)
                    return Array.Empty<RemoteImageInfo>();

                start += 7;
                int end = malId.IndexOf('/', start);
                if (end < 0)
                    return Array.Empty<RemoteImageInfo>();

                if (!long.TryParse(malId.AsSpan(start, end - start), out aid))
                    return Array.Empty<RemoteImageInfo>();
            }
            else
            {
                if (!long.TryParse(malId, out aid))
                    return Array.Empty<RemoteImageInfo>();
            }

            var media = new AnimeObject { anime = await JikanAPI.GetAnimeFullAsync(aid, cancellationToken).ConfigureAwait(false) };
            var pictures = await JikanAPI.GetAnimePicturesAsync(aid, cancellationToken).ConfigureAwait(false);

            var images = new List<RemoteImageInfo>();

            var mainImageUrl = media.GetImageUrl(media.anime.Images.JPG);
            if (!string.IsNullOrEmpty(mainImageUrl))
            {
                images.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Primary,
                    Url = mainImageUrl
                });
            }

            foreach (var pic in pictures)
            {
                var picUrl = media.GetImageUrl(pic.JPG);

                if (string.IsNullOrEmpty(picUrl) || picUrl == mainImageUrl)
                    continue;

                images.Add(new RemoteImageInfo
                {
                    ProviderName = Name,
                    Type = ImageType.Primary,
                    Url = picUrl
                });
            }
            return (images.Count == 0) ? Array.Empty<RemoteImageInfo>() : images;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClient.GetAsync(url, cancellationToken);
        }
    }
}
