using Jellyfin.Plugin.MyAnimeList.Configuration;
using JikanDotNet;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder
    {
        private readonly ILogger _log;
        private readonly Jikan _jikan;
        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListMovieProvider(ILogger<MyAnimeListMovieProvider> logger)
        {
            _log = logger;
            _jikan = NewJikan._jikan;
        }

        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Movie>();
            Anime media = new Anime();
            PluginConfiguration config = Plugin.Instance.Configuration;

            string straid = info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList);
            if (!string.IsNullOrEmpty(straid))
            {
                _log.LogInformation("Populating Movie metadata for: {aid}", straid);
                long aid = long.Parse(straid);
                media.anime = (await _jikan.GetAnimeAsync(aid, cancellationToken)).Data;
                media.characters = (await _jikan.GetAnimeCharactersAsync(aid, cancellationToken)).Data;
            }
            else
            {
                string searchName = MyAnimelistSearchHelper.PreprocessTitle(info.Name);
                _log.LogInformation("Populating Movie metadata for: {Name}", searchName);
                AnimeSearchConfig searchConfig = new AnimeSearchConfig();
                searchConfig.Query = searchName;
                searchConfig.Type = AnimeType.Movie;
                JikanDotNet.Anime anime = (await _jikan.SearchAnimeAsync(searchName, cancellationToken)).Data
                    .Where(a => a.Type == null || !a.Type.Equals(AnimeType.TV.ToString()))
                    .FirstOrDefault();
                if (anime != null)
                {
                    long aid = anime.MalId.Value;
                    info.ProviderIds.Add(ProviderNames.MyAnimeList, aid.ToString());
                    media.anime = (await _jikan.GetAnimeAsync(aid, cancellationToken)).Data;
                    media.characters = (await _jikan.GetAnimeCharactersAsync(aid, cancellationToken)).Data;
                }
            }

            if (media != null)
            {
                result.HasMetadata = true;
                result.Item = media.ToMovie();
                result.People = media.GetPeopleInfo();
                result.Provider = ProviderNames.MyAnimeList;
            }

            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
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
