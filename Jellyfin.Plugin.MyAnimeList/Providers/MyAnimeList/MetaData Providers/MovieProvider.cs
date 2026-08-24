using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MovieProvider : BaseProvider<Movie, MovieInfo>
    {
        public MovieProvider(ILogger<MovieProvider> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory) : base(logger, libraryManager, httpClientFactory)
        {
        }

        protected override Movie ConvertToItem(AnimeObject media, MovieInfo info) => media.ToMovie(info);
    }
}
