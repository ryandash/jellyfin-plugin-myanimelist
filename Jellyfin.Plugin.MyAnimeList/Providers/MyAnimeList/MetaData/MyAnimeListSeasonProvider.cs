using Emby.Naming.TV;
using Jellyfin.Plugin.MyAnimeList.Configuration;
using JikanDotNet;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
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
            Anime media = new Anime();
            PluginConfiguration config = Plugin.Instance.Configuration;
            string straid = info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList);
            int seasonNumber = 1;

            if (!string.IsNullOrEmpty(straid))
            {
                _log.LogInformation("Start MyAnimeList... Searching({straid})", straid);
                media = (await GetAnimeInfo(long.Parse(straid), cancellationToken));
                seasonNumber = info.IndexNumber.HasValue ? info.IndexNumber.Value : 1;
            }
            else
            {
                string[] splitPath = info.Path.Split("\\");
                string part1 = splitPath[^1];
                string searchName = part1.Contains("season", StringComparison.OrdinalIgnoreCase)
                    ? Anitomy.AnitomyHelper.ExtractAnimeTitle(MyAnimelistSearchHelper.PreprocessTitle(splitPath[^2]))
                    : Anitomy.AnitomyHelper.ExtractAnimeTitle(MyAnimelistSearchHelper.PreprocessTitle(part1));

                _log.LogInformation("Start MyAnimeList... Searching({Name})", searchName);
                if (part1.Contains("season", StringComparison.OrdinalIgnoreCase))
                {
                    int season;
                    if (int.TryParse(Anitomy.AnitomyHelper.ExtractSeasonNumber(part1), out season))
                    {
                        seasonNumber = season;
                    }
                }
                JikanDotNet.Anime anime = (await _jikan.SearchAnimeAsync(searchName, cancellationToken)).Data
                    .Where(a => a.Type == null || !a.Type.Equals(AnimeType.Movie.ToString()))
                    .FirstOrDefault();

                if (anime != null)
                {
                    long aid = anime.MalId.Value;
                    for (int i = 1; i < seasonNumber; i++)
                    {
                        RelatedEntry entry = (await _jikan.GetAnimeRelationsAsync(aid, cancellationToken))
                            .Data
                            .FirstOrDefault(r => r.Relation.Equals("Sequel", StringComparison.OrdinalIgnoreCase));

                        if (entry == null) break;

                        aid = entry.Entry.FirstOrDefault()?.MalId ?? aid;

                        anime = (await _jikan.GetAnimeAsync(aid, cancellationToken)).Data;
                        if (anime.Titles.Any(t => t.Title.Contains("part ", StringComparison.OrdinalIgnoreCase)))
                        {
                            seasonNumber++;
                        }

                    }
                    media = (await GetAnimeInfo(aid, cancellationToken));
                }
            }

            if (media.anime != null)
            {
                result.HasMetadata = true;
                result.Item = media.ToSeason(seasonNumber);
                result.People = media.GetPeopleInfo();
                result.Provider = ProviderNames.MyAnimeList;
            }

            return result;
        }

        private async Task<Anime> GetAnimeInfo(long aid, CancellationToken cancellationToken)
        {
            Anime media = new Anime();
            media.anime = (await _jikan.GetAnimeAsync(aid, cancellationToken)).Data;
            media.characters = (await _jikan.GetAnimeCharactersAsync(aid, cancellationToken)).Data;
            return media;
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
