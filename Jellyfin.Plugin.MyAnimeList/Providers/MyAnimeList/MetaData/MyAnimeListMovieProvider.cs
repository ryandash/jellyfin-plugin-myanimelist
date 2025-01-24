using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using JikanDotNet;
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
        private readonly Jikan _jikan;
        private MyAnimeListSearchHelper _searchHelper;
        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListMovieProvider(ILogger<MyAnimeListMovieProvider> logger)
        {
            _log = logger;
            _jikan = JikanSingleton.Instance;
            _searchHelper = new MyAnimeListSearchHelper(_jikan);
        }

        public async Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
        {
            long? malId = _searchHelper.GetAnimeIdAsync(_log, info, cancellationToken).Result;
            MetadataResult<Movie> result = new MetadataResult<Movie>();
            if (!malId.HasValue) return result;

            var media = await GetAnimeInfoAsync(malId.Value, info, cancellationToken).ConfigureAwait(false);
            if (media == null || media.anime == null) return result;

            result.HasMetadata = true;
            result.Item = media.ToMovie();
            result.People = media.GetPeopleInfo();
            result.Provider = ProviderNames.MyAnimeList;
            return result;
        }

        private async Task<Anime> GetAnimeInfoAsync(long malId, MovieInfo info, CancellationToken cancellationToken)
        {
            _log.LogInformation("Populating Movie metadata for: {straid}", malId);
            var media = new Anime
            {
                anime = (await _jikan.GetAnimeAsync(malId, cancellationToken).ConfigureAwait(false)).Data,
                characters = (await _jikan.GetAnimeCharactersAsync(malId, cancellationToken).ConfigureAwait(false)).Data
            };
            return media;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo info, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            long? aid = _searchHelper.GetAnimeIdAsync(_log, info, cancellationToken).Result;
            if (aid.HasValue)
            {
                AnimeSearchResult aid_result = new AnimeSearchResult();
                aid_result.anime = (await _jikan.GetAnimeAsync(aid.Value, cancellationToken).ConfigureAwait(false)).Data;
                if (aid_result.anime != null)
                {
                    results.Add(aid_result.ToSearchResult());
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
