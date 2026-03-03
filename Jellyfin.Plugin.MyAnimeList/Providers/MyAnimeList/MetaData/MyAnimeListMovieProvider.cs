using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListMovieProvider : MyAnimeListBaseProvider<Movie, MovieInfo>
    {
        public MyAnimeListMovieProvider(ILogger<MyAnimeListMovieProvider> logger, ILibraryManager libraryManager) : base(logger, libraryManager)
        {
        }

        protected override Movie ConvertToItem(Anime media)
            => media.ToMovie();
    }
}
