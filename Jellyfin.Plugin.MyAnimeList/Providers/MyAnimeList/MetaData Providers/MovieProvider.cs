using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MovieProvider : BaseProvider<MediaBrowser.Controller.Entities.Movies.Movie, MovieInfo>
    {
        public MovieProvider(ILogger<MovieProvider> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory) : base(logger, libraryManager, httpClientFactory)
        {
        }

        protected override MediaBrowser.Controller.Entities.Movies.Movie ConvertToItem(AnimeObject media, ItemLookupInfo info)
            => media.ToMovie(info as MovieInfo);
    }
}
