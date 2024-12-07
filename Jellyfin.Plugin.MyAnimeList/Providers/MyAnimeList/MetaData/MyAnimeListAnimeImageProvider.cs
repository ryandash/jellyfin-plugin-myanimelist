using Jellyfin.Plugin.MyAnimeList.Configuration;
using JikanDotNet;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
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
            _jikan = NewJikan._jikan;
            _log = logger;
        }

        public string Name => "MyAnimeList";

        public bool Supports(BaseItem item) => item is Series || item is Season || item is Movie;

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new[] { ImageType.Primary };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            var straid = item.GetProviderId(ProviderNames.MyAnimeList);
            if (string.IsNullOrEmpty(straid))
            {
                return list;
            }

            PluginConfiguration config = Plugin.Instance.Configuration;
            Anime media = new Anime();
            long aid = long.Parse(straid);

            if (item is Season season && season?.Path != null)
            {
                int seasonNumber = 1;
                string[] splitPath = season.Path.Split("\\");
                string part1 = splitPath[^1];
                string searchName = part1.Contains("season", StringComparison.OrdinalIgnoreCase)
                    ? Anitomy.AnitomyHelper.ExtractAnimeTitle(MyAnimelistSearchHelper.PreprocessTitle(splitPath[^2]))
                    : Anitomy.AnitomyHelper.ExtractAnimeTitle(MyAnimelistSearchHelper.PreprocessTitle(part1));

                _log.LogInformation("Start MyAnimeList... Searching({searchName})", searchName);
                if (part1.Contains("season", StringComparison.OrdinalIgnoreCase))
                {
                    int seasonNum;
                    if (int.TryParse(Anitomy.AnitomyHelper.ExtractSeasonNumber(part1), out seasonNum))
                    {
                        seasonNumber = seasonNum;
                    }
                }

                for (int i = 1; i < seasonNumber; i++)
                {
                    var entry = (await _jikan.GetAnimeRelationsAsync(aid, cancellationToken))?.Data
                        .FirstOrDefault(r => r.Relation.Equals("Sequel", StringComparison.OrdinalIgnoreCase));

                    if (entry?.Entry?.FirstOrDefault()?.MalId == null)
                        break;

                    aid = entry.Entry.FirstOrDefault()?.MalId ?? aid;

                    var anime = await _jikan.GetAnimeAsync(aid, cancellationToken);
                    if (anime?.Data?.Titles.Any(t => t.Title.Contains("part ", StringComparison.OrdinalIgnoreCase)) == true)
                    {
                        seasonNumber++;
                    }
                }
            }

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
