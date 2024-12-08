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
            Anime media = new Anime();
            PluginConfiguration config = Plugin.Instance.Configuration;
            string straid = info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList);

            if (!string.IsNullOrEmpty(straid))
            {
                _log.LogInformation("Populating Series metadata for: {straid}", straid);
                media = await GetAnimeInfo(long.Parse(straid), cancellationToken);
            }
            else
            {
                string searchName = MyAnimelistSearchHelper.PreprocessTitle(info.Name);
                _log.LogInformation("Populating Series metadata for: {Name}", searchName);
                var anime = (await _jikan.SearchAnimeAsync(searchName, cancellationToken)).Data
                    .Where(a => a.Type == null || !a.Type.Equals(AnimeType.Movie.ToString()))
                    .FirstOrDefault();

                if (anime != null && anime.MalId.HasValue)
                {
                    media = await GetAnimeInfo(anime.MalId.Value, cancellationToken);
                }
            }

            if (media != null && media.anime != null)
            {
                result.HasMetadata = true;
                result.Item = media.ToSeries();
                result.People = media.GetPeopleInfo();
                result.Provider = ProviderNames.MyAnimeList;
            }

            return result;
        }


        private async Task<Anime> GetAnimeInfo(long aid, CancellationToken cancellationToken)
        {
            Anime media = new Anime();
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
                AnimeSearchResult aid_result = new AnimeSearchResult();
                aid_result.anime = (await _jikan.GetAnimeAsync(aid, cancellationToken).ConfigureAwait(false)).Data;
                if (aid_result.anime != null)
                {
                    results.Add(aid_result.ToSearchResult());
                }
            }

            if (!string.IsNullOrEmpty(searchInfo.Name))
            {
                ICollection<AnimeSearchResult> animeList = (ICollection<AnimeSearchResult>)(await _jikan.SearchAnimeAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false)).Data;
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
