using Jellyfin.Plugin.MyAnimeList.Anitomy;
using JikanDotNet;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Season = MediaBrowser.Controller.Entities.TV.Season;
using SeasonInfo = MediaBrowser.Controller.Providers.SeasonInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListSeasonProvider : IRemoteMetadataProvider<Season, SeasonInfo>, IHasOrder
    {
        private readonly ILogger<MyAnimeListSeasonProvider> _log;
        private readonly Jikan _jikan;
        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListSeasonProvider(ILogger<MyAnimeListSeasonProvider> logger)
        {
            _log = logger;
            _jikan = NewJikan._jikan;
        }

        public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Season>();
            var malId = info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList);
            Anime media = await GetAnimeInfoAsync(malId, info, cancellationToken).ConfigureAwait(false);

            if (media == null || media.anime == null)
            {
                return result;
            }

            result.HasMetadata = true;
            result.Item = media.ToSeason();
            result.People = media.GetPeopleInfo();
            result.Provider = ProviderNames.MyAnimeList;
            return result;
        }

        private async Task<Anime> GetAnimeInfoAsync(string malId, SeasonInfo info, CancellationToken cancellationToken)
        {
            Anime media = new Anime();
            if (!string.IsNullOrEmpty(malId))
            {
                var aid = long.Parse(malId);
                _log.LogInformation("Populating Season metadata for: {straid}", malId);
                media.anime = (await _jikan.GetAnimeAsync(aid, cancellationToken).ConfigureAwait(false)).Data;
            }
            else
            {
                if (info.Path == null || !info.IndexNumber.HasValue) return null;
                string[] splitPath = info.Path.Split("\\");
                string part1 = splitPath[^1];
                string searchName = part1.Contains("season", StringComparison.OrdinalIgnoreCase)
                    ? MyAnimelistSearchHelper.PreprocessTitle(splitPath[^2])
                    : MyAnimelistSearchHelper.PreprocessTitle(part1);
                AnitomyHelper animeInfo = new AnitomyHelper(searchName);
                var anime = (await _jikan.SearchAnimeAsync(animeInfo.AnimeTitle, cancellationToken).ConfigureAwait(false))?.Data
                            .FirstOrDefault(a => !a.Type.Equals(AnimeType.Movie.ToString()));
                media.anime = await GetAnimeBySeasonAsync(anime, info.IndexNumber.Value, cancellationToken).ConfigureAwait(false);
            }
            media.characters = (await _jikan.GetAnimeCharactersAsync(media.anime.MalId.Value, cancellationToken).ConfigureAwait(false)).Data;
            return media;
        }

        private async Task<JikanDotNet.Anime> GetAnimeBySeasonAsync(JikanDotNet.Anime anime, int seasonNumber, CancellationToken cancellationToken)
        {
            if (anime == null) return null;

            long aid = anime.MalId.Value;
            for (int i = 1; i < seasonNumber; i++)
            {
                RelatedEntry relation = (await _jikan.GetAnimeRelationsAsync(aid, cancellationToken).ConfigureAwait(false))?.Data
                               .FirstOrDefault(r => r.Relation.Equals("Sequel", StringComparison.OrdinalIgnoreCase));
                if (relation == null) break;

                MalUrl malurl = relation.Entry.FirstOrDefault();
                if (malurl == null) break;

                aid = malurl.MalId;
                anime = (await _jikan.GetAnimeAsync(aid, cancellationToken).ConfigureAwait(false)).Data;

                if (anime.Titles.Any(t => t.Title.Contains("part ", StringComparison.OrdinalIgnoreCase)))
                {
                    seasonNumber++;
                }
            }

            return anime;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken cancellationToken)
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
