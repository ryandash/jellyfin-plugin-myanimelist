using Jellyfin.Plugin.MyAnimeList.Configuration;
using JikanDotNet;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Series = MediaBrowser.Controller.Entities.TV.Series;
using SeriesInfo = MediaBrowser.Controller.Providers.SeriesInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
    {
        private readonly ILogger<MyAnimeListSeriesProvider> _log;
        private readonly Jikan _jikan;
        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListSeriesProvider(ILogger<MyAnimeListSeriesProvider> logger)
        {
            _log = logger;
            _jikan = NewJikan._jikan;
        }

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            Media media = new Media();
            PluginConfiguration config = Plugin.Instance.Configuration;
            string straid = info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList);

            if (!string.IsNullOrEmpty(straid))
            {
                media = GetAnimeInfo(long.Parse(straid), cancellationToken).Result;
            }
            else
            {
                string searchName = Anitomy.AnitomyHelper.ExtractAnimeTitle(MyAnimelistSearchHelper.PreprocessTitle(info.Name));
                _log.LogInformation("Start MyAnimeList... Searching({Name})", searchName);
                Anime anime = (await _jikan.SearchAnimeAsync(searchName, cancellationToken)).Data.Where(a => !a.Type.Equals(AnimeType.Movie.ToString())).FirstOrDefault();
                if (anime != null)
                {
                    media = GetAnimeInfo(anime.MalId.Value, cancellationToken).Result;
                }
            }

            if (media.anime != null)
            {
                result.HasMetadata = true;
                result.Item = media.ToSeries();
                result.People = media.GetPeopleInfo();
                result.Provider = ProviderNames.MyAnimeList;
            }

            return result;
        }

        private async Task<Media> GetAnimeInfo(long aid, CancellationToken cancellationToken)
        {
            Media media = new Media();
            media.anime = (await _jikan.GetAnimeAsync(aid, cancellationToken)).Data;
            media.characters = (await _jikan.GetAnimeCharactersAsync(aid, cancellationToken)).Data;
            return media;
        }


        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            var straid = searchInfo.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList);
            if (!string.IsNullOrEmpty(straid))
            {
                long aid = long.Parse(straid);
                MediaSearchResult aid_result = new MediaSearchResult();
                aid_result.anime = (await _jikan.GetAnimeAsync(aid, cancellationToken).ConfigureAwait(false)).Data;
                if (aid_result.anime != null)
                {
                    results.Add(aid_result.ToSearchResult());
                }
            }

            if (!string.IsNullOrEmpty(searchInfo.Name))
            {
                ICollection<MediaSearchResult> animeList = (ICollection<MediaSearchResult>)(await _jikan.SearchAnimeAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false)).Data;
                if (animeList != null)
                {
                    foreach (var media in animeList)
                    {
                        results.Add(media.ToSearchResult());
                    }
                }
            }

            return results;
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = Plugin.Instance.GetHttpClient();
            return await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
    }
}
