using Jellyfin.Plugin.MyAnimeList.Configuration;
using JikanDotNet;
using JikanDotNet.Config;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

//API v2
namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
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
            _jikan = new Jikan();
        }

        public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Series>();
            Media media = new Media();
            PluginConfiguration config = Plugin.Instance.Configuration;

            string straid = info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList);
            if (!string.IsNullOrEmpty(straid))
            {
                long aid = long.Parse(straid);
                media.anime = (await _jikan.GetAnimeAsync(aid)).Data;
                media.characters = (await _jikan.GetAnimeCharactersAsync(aid)).Data;
            }
            else
            {
                string searchName = MyAnimelistSearchHelper.PreprocessTitle(info.Name);
                if (config.UseAnitomyLibrary)
                {
                    searchName = Anitomy.AnitomyHelper.ExtractAnimeTitle(searchName);
                }

                _log.LogInformation("Start MyAnimeList... Searching({Name})", searchName);
                ICollection<Anime> animeList = (await _jikan.SearchAnimeAsync(searchName, cancellationToken)).Data;

                if (animeList != null)
                {
                    Anime bestAnime = DictionaryExtensions.GetBestAnime(searchName, animeList);
                    if (bestAnime?.MalId != null)
                    {
                        long aid = bestAnime.MalId.Value;
                        info.ProviderIds.Add(ProviderNames.MyAnimeList, aid.ToString());
                        media.anime = (await _jikan.GetAnimeAsync(aid)).Data;
                        media.characters = (await _jikan.GetAnimeCharactersAsync(aid)).Data;
                    }
                }
            }

            if (media.anime != null)
            {
                result.HasMetadata = true;
                result.Item = media.ToSeries();
                result.People = media.GetPeopleInfo();
                result.Provider = ProviderNames.MyAnimeList;
                result.RemoteImages = new List<(string, ImageType)>
                {
                    (media.GetImageUrl(), ImageType.Primary)
                };
            }

            return result;
        }


        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            var straid = searchInfo.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList);
            if (!string.IsNullOrEmpty(straid))
            {
                long aid = long.Parse(straid);
                MediaSearchResult aid_result = new MediaSearchResult();
                aid_result.anime = (await _jikan.GetAnimeAsync(aid).ConfigureAwait(false)).Data;
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
            return await httpClient.GetAsync(url).ConfigureAwait(false);
        }
    }
}
