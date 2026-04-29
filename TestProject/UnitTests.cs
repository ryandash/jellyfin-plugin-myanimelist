using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using TestProject;
using Xunit.Abstractions;
using EpisodeInfo = MediaBrowser.Controller.Providers.EpisodeInfo;
using SeriesInfo = MediaBrowser.Controller.Providers.SeriesInfo;

namespace UnitTestProject
{
    public class MyAnimeListTests
    {
        private readonly Mock<ILogger> _log;
        private readonly Mock<ILibraryManager> _libraryManagerMock;
        private readonly MyAnimeListSearchHelper _searchHelper;
        private readonly ITestOutputHelper _output;
        private readonly string libraryLocation = "D:\\Anime\\";

        public MyAnimeListTests(ITestOutputHelper output)
        {
            _log = new Mock<ILogger>();
            _libraryManagerMock = new Mock<ILibraryManager>();
            var mockFolder = new VirtualFolderInfo { Name = "Anime", CollectionType = CollectionTypeOptions.tvshows };
            mockFolder.Locations = [libraryLocation];
            _libraryManagerMock.Setup(m => m.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo> { mockFolder });

            JikanAPI.Initialize(new FakeApplicationPaths(), new PluginConfiguration());

            _searchHelper = new MyAnimeListSearchHelper(_libraryManagerMock.Object);
            _output = output;
        }

        [Fact]
        public async Task GetMalIdHardSeries()
        {
            // Synonym name search
            SeriesInfo seriesInfo = new SeriesInfo
            {
                Path = Path.Combine(libraryLocation, "Nigetsuri"),
                IndexNumber = 1
            };

            AnimeFullCacheDto anime = await _searchHelper.GetAnimeAsync(_log.Object, seriesInfo, CancellationToken.None, false);
            Assert.NotNull(anime);
            Assert.NotNull(anime.MalId);
            _output.WriteLine(anime.MalId.Value.ToString());
            // Result: Nigashita Sakana wa Ookikatta ga Tsuriageta Sakana ga Ookisugita Ke
            Assert.Equal(62893, anime.MalId.Value);


            // Non standard name for MyAnimeList
            SeriesInfo seriesInfo2 = new SeriesInfo
            {
                Path = Path.Combine(libraryLocation, "Initial D"),
                IndexNumber = 1
            };

            anime = await _searchHelper.GetAnimeAsync(_log.Object, seriesInfo2, CancellationToken.None, false);
            Assert.NotNull(anime);
            Assert.NotNull(anime.MalId);
            _output.WriteLine(anime.MalId.Value.ToString());
            // Result: Initial D First Stage
            Assert.Equal(185, anime.MalId.Value);
        }

        [Fact]
        public async Task GetMalIdHardSeasons()
        {
            // Movie season
            SeasonInfo seasonInfo = new SeasonInfo
            {
                Path = Path.Combine(libraryLocation, "Initial D"),
                IndexNumber = 3
            };

            AnimeFullCacheDto anime = await _searchHelper.GetAnimeAsync(_log.Object, seasonInfo, CancellationToken.None, false);
            Assert.NotNull(anime);
            Assert.NotNull(anime.MalId);
            _output.WriteLine(anime.MalId.Value.ToString());
            // Result: Initial D Third Stage (Movie)
            Assert.Equal(187, anime.MalId.Value);


            // Final season after movie
            SeasonInfo seasonInfo2 = new SeasonInfo
            {
                Path = Path.Combine(libraryLocation, "Initial D"),
                IndexNumber = 6
            };

            anime = await _searchHelper.GetAnimeAsync(_log.Object, seasonInfo2, CancellationToken.None, false);
            Assert.NotNull(anime);
            Assert.NotNull(anime.MalId);
            _output.WriteLine(anime.MalId.Value.ToString());
            // Result: Initial D Final Stage
            Assert.Equal(22507, anime.MalId.Value);


            // seasons with parts
            SeasonInfo seasonInfo3 = new SeasonInfo
            {
                Path = Path.Combine(libraryLocation, "Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka"),
                IndexNumber = 6
            };

            anime = await _searchHelper.GetAnimeAsync(_log.Object, seasonInfo3, CancellationToken.None, false);
            Assert.NotNull(anime);
            Assert.NotNull(anime.MalId);
            _output.WriteLine(anime.MalId.Value.ToString());
            // Result: Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka 6th Season
            Assert.Equal(63442, anime.MalId.Value);


            // seasons with movies and tv series intertwined
            SeasonInfo seasonInfo4 = new SeasonInfo
            {
                Path = Path.Combine(libraryLocation, "Kimetsu no Yaiba"),
                IndexNumber = 5
            };

            anime = await _searchHelper.GetAnimeAsync(_log.Object, seasonInfo4, CancellationToken.None, false);
            Assert.NotNull(anime);
            Assert.NotNull(anime.MalId);
            _output.WriteLine(anime.MalId.Value.ToString());
            // Result: Kimetsu no Yaiba: Hashira Geiko-hen
            Assert.Equal(55701, anime.MalId.Value);
        }

        [Fact]
        public async Task GetMalIdHardEpisodes()
        {
            // season with parts
            EpisodeInfo episodeInfo = new EpisodeInfo
            {
                Path = Path.Combine(libraryLocation, "Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka"),
                ParentIndexNumber = 4,
                IndexNumber = 12
            };

            AnimeFullCacheDto anime = await _searchHelper.GetAnimeAsync(_log.Object, episodeInfo, CancellationToken.None, false);
            Assert.NotNull(anime);
            Assert.NotNull(anime.MalId);
            _output.WriteLine(anime.MalId.Value.ToString());
            // Result: Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka IV: Shin Shou - Meikyuu-hen
            Assert.Equal(47164, anime.MalId.Value);

            (int episodeNumber, AnimeFullCacheDto updatedAnime) = await _searchHelper.GetSeasonEpisodeNumberAsync(
                    _log.Object,
                    episodeInfo.IndexNumber.Value,
                    episodeInfo.ParentIndexNumber.Value,
                    anime,
                    CancellationToken.None
                );

            Assert.NotNull(updatedAnime);
            Assert.NotNull(updatedAnime.MalId);
            Assert.NotEqual(0, episodeNumber);
            // Result: Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka IV: Shin Shou - Yakusai-hen
            Assert.Equal(53111, updatedAnime.MalId.Value);
            Assert.Equal(1, episodeNumber);
        }
    }
}
