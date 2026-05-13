using System.Net.Http;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListMovieProvider : MyAnimeListBaseProvider<Movie, MovieInfo>
    {
        public MyAnimeListMovieProvider(ILogger<MyAnimeListMovieProvider> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory) : base(logger, libraryManager, httpClientFactory)
        {
        }

        protected override Movie ConvertToItem(AnimeObject media)
            => media.ToMovie();
    }
}
