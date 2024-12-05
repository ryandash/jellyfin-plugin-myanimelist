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
            var straid = item.GetProviderId(ProviderNames.MyAnimeList);
            var list = new List<RemoteImageInfo>();
            PluginConfiguration config = Plugin.Instance.Configuration;

            if (!string.IsNullOrEmpty(straid))
            {
                Media media = new Media();
                long aid = long.Parse(straid);

                if (item is Season season)
                {
                    string[] splitPath = season.Path.Split("\\");
                    string searchName = Anitomy.AnitomyHelper.ExtractAnimeTitle(
                        MyAnimelistSearchHelper.PreprocessTitle(splitPath[splitPath.Length - 2])
                    );
                    int seasonNumber = int.Parse(Anitomy.AnitomyHelper.ExtractSeasonNumber(splitPath[splitPath.Length - 1]));
                    for (int i = 1; i < seasonNumber; i++)
                    {
                        RelatedEntry entry = (await _jikan.GetAnimeRelationsAsync(aid, cancellationToken))
                            .Data
                            .FirstOrDefault(r => r.Relation.Equals("Sequel", StringComparison.OrdinalIgnoreCase));

                        if (entry == null) break;

                        aid = entry.Entry.FirstOrDefault()?.MalId ?? aid;

                        var anime = (await _jikan.GetAnimeAsync(aid, cancellationToken)).Data;
                        if (anime.Titles.Any(t => t.Title.Contains("part ", StringComparison.OrdinalIgnoreCase)))
                        {
                            seasonNumber++;
                        }
                    }
                }

                media.anime = (await _jikan.GetAnimeAsync(aid, cancellationToken)).Data;
                _ = (await _jikan.GetAnimePicturesAsync(aid, cancellationToken)).Data;
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
