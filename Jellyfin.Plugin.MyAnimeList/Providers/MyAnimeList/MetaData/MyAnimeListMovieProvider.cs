using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, IHasOrder
    {
        private readonly ILogger _log;
        private readonly MyAnimeListSearchHelper _searchHelper;
        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListMovieProvider(ILogger<MyAnimeListMovieProvider> logger)
        {
            _log = logger;
            _searchHelper = new MyAnimeListSearchHelper();
        }

        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            MetadataResult<Movie> result = new MetadataResult<Movie>();
            JikanDotNet.Anime anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken);
            if (anime == null) return result;

            Anime media = new Anime
            {
                anime = anime,
                characters = (await JikanSingleton.GetAnimeCharactersAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false))?.Data
            };

            result.HasMetadata = true;
            result.Item = media.ToMovie();
            result.People = media.GetPeopleInfo();
            result.Provider = ProviderNames.MyAnimeList;
            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo info, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            JikanDotNet.Anime anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken);
            if (anime == null) return results;

            AnimeSearchResult aid_result = new AnimeSearchResult();
            aid_result.anime = anime;
            results.Add(aid_result.ToSearchResult());

            return results;
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = Plugin.Instance.GetHttpClient();
            return await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
    }
}
