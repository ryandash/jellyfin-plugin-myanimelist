using Jellyfin.Plugin.MyAnimeList.Providers;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.Helpers;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using TestProject;
using EpisodeInfo = MediaBrowser.Controller.Providers.EpisodeInfo;
using SeriesInfo = MediaBrowser.Controller.Providers.SeriesInfo;

namespace UnitTestProject
{
    public class MyAnimeListTests
    {
        private readonly ILogger _log;
        private readonly Mock<ILibraryManager> _libraryManagerMock;
        private readonly SearchHelper _searchHelper;
        private readonly string libraryLocation = "D:\\Anime\\";

        public MyAnimeListTests()
        {
            _log = new NUnitLogger();

            _libraryManagerMock = new Mock<ILibraryManager>();

            var mockFolder = new VirtualFolderInfo
            {
                Name = "Anime",
                CollectionType = CollectionTypeOptions.tvshows,
                Locations = [libraryLocation]
            };

            _libraryManagerMock
                .Setup(m => m.GetVirtualFolders())
                .Returns(new List<VirtualFolderInfo> { mockFolder });

            JikanAPI.Initialize(new FakeApplicationPaths());

            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            services.AddHttpClient(ProviderNames.MyAnimeList, client =>
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UnitTest");
            });

            _searchHelper = new SearchHelper(_libraryManagerMock.Object);
        }

        [Test]
        [Order(1)]
        public async Task GetSeriesNigetsuri()
        {
            // Synonym name search
            SeriesInfo seriesInfo = new SeriesInfo
            {
                Path = Path.Combine(libraryLocation, "Nigetsuri"),
                IndexNumber = 1
            };

            AnimeFullCacheDto anime = await _searchHelper.GetAnimeAsync(_log, seriesInfo, CancellationToken.None, false);
            Assert.That(anime, Is.Not.Null);
            Assert.That(anime.MalId, Is.Not.Null);
            TestContext.Progress.WriteLine(anime.MalId.Value.ToString());
            // Result: Nigashita Sakana wa Ookikatta ga Tsuriageta Sakana ga Ookisugita Ke
            Assert.That(anime.MalId.Value, Is.EqualTo(62893));
        }

        [Test]
        [Order(2)]
        public async Task GetSeriesInitialD()
        {
            // Non standard name for MyAnimeList
            SeriesInfo seriesInfo2 = new SeriesInfo
            {
                Path = Path.Combine(libraryLocation, "Initial D"),
                IndexNumber = 1
            };

            AnimeFullCacheDto anime = await _searchHelper.GetAnimeAsync(_log, seriesInfo2, CancellationToken.None, false);
            Assert.That(anime, Is.Not.Null);
            Assert.That(anime.MalId, Is.Not.Null);
            TestContext.Progress.WriteLine(anime.MalId.Value.ToString());
            // Result: Initial D First Stage
            Assert.That(anime.MalId.Value, Is.EqualTo(185));
        }

        [Test]
        [Order(3)]
        public async Task GetSeasonInitialDMovie()
        {
            // Movie season
            SeasonInfo seasonInfo = new SeasonInfo
            {
                Path = Path.Combine(libraryLocation, "Initial D"),
                IndexNumber = 3
            };

            AnimeFullCacheDto anime = await _searchHelper.GetAnimeAsync(_log, seasonInfo, CancellationToken.None, false);
            Assert.That(anime, Is.Not.Null);
            Assert.That(anime.MalId, Is.Not.Null);
            TestContext.Progress.WriteLine(anime.MalId.Value.ToString());
            // Result: Initial D Third Stage (Movie)
            Assert.That(anime.MalId.Value, Is.EqualTo(187));

        }

        [Test]
        [Order(4)]
        public async Task GetSeasonInitialDFinal()
        {
            // Final season after movie
            SeasonInfo seasonInfo2 = new SeasonInfo
            {
                Path = Path.Combine(libraryLocation, "Initial D"),
                IndexNumber = 6
            };

            AnimeFullCacheDto anime = await _searchHelper.GetAnimeAsync(_log, seasonInfo2, CancellationToken.None, false);
            Assert.That(anime, Is.Not.Null);
            Assert.That(anime.MalId, Is.Not.Null);
            TestContext.Progress.WriteLine(anime.MalId.Value.ToString());
            // Result: Initial D Final Stage
            Assert.That(anime.MalId.Value, Is.EqualTo(22507));
        }

        [Test]
        [Order(5)]
        public async Task GetSeasonDanmachi()
        {
            // seasons with parts
            SeasonInfo seasonInfo3 = new SeasonInfo
            {
                Path = Path.Combine(libraryLocation, "Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka"),
                IndexNumber = 6
            };

            AnimeFullCacheDto anime = await _searchHelper.GetAnimeAsync(_log, seasonInfo3, CancellationToken.None, false);
            Assert.That(anime, Is.Not.Null);
            Assert.That(anime.MalId, Is.Not.Null);
            TestContext.Progress.WriteLine(anime.MalId.Value.ToString());
            // Result: Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka 6th Season
            Assert.That(anime.MalId.Value, Is.EqualTo(63442));
        }

        [Test]
        [Order(6)]
        public async Task GetSeasonKimetsu()
        {

            // seasons with movies and tv series intertwined
            SeasonInfo seasonInfo4 = new SeasonInfo
            {
                Path = Path.Combine(libraryLocation, "Kimetsu no Yaiba"),
                IndexNumber = 5
            };

            AnimeFullCacheDto anime = await _searchHelper.GetAnimeAsync(_log, seasonInfo4, CancellationToken.None, false);
            Assert.That(anime, Is.Not.Null);
            Assert.That(anime.MalId, Is.Not.Null);
            TestContext.Progress.WriteLine(anime.MalId.Value.ToString());
            // Result: Kimetsu no Yaiba: Hashira Geiko-hen
            Assert.That(anime.MalId.Value, Is.EqualTo(55701));
        }

        [Test]
        [Order(7)]
        public async Task GetEpisodeDanmachi()
        {
            // season with parts
            EpisodeInfo episodeInfo = new EpisodeInfo
            {
                Name = "Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka",
                ParentIndexNumber = 4,
                IndexNumber = 12
            };

            AnimeFullCacheDto anime = await _searchHelper.GetAnimeAsync(_log, episodeInfo, CancellationToken.None, false);
            Assert.That(anime, Is.Not.Null);
            Assert.That(anime.MalId, Is.Not.Null);
            TestContext.Progress.WriteLine(anime.MalId.Value.ToString());
            // Result: Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka IV: Shin Shou - Meikyuu-hen
            Assert.That(anime.MalId.Value, Is.EqualTo(47164));

            (int episodeNumber, AnimeFullCacheDto updatedAnime) = await RelationsResolver.GetSeasonEpisodeNumberAsync(
                    _log,
                    episodeInfo.IndexNumber.Value,
                    episodeInfo.ParentIndexNumber.Value,
                    anime,
                    CancellationToken.None
                );
            Assert.That(updatedAnime, Is.Not.Null);
            Assert.That(updatedAnime.MalId, Is.Not.Null);
            Assert.That(episodeNumber, Is.Not.EqualTo(0));
            // Result: Dungeon ni Deai wo Motomeru no wa Machigatteiru Darou ka IV: Shin Shou - Yakusai-hen
            Assert.That(updatedAnime.MalId.Value, Is.EqualTo(53111));
            Assert.That(episodeNumber, Is.EqualTo(1));
        }

        [Test]
        [Order(8)]
        public async Task SaveCache()
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
        }
    }
}
