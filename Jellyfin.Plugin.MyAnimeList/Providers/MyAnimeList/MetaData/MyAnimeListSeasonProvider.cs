using Jellyfin.Plugin.MyAnimeList.Configuration;
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
            Media media = new Media();
            PluginConfiguration config = Plugin.Instance.Configuration;
            string straid = info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList);

            if (!string.IsNullOrEmpty(straid))
            {
                media = (await GetAnimeInfo(long.Parse(straid), cancellationToken));
            }
            else
            {
                string[] splitPath = info.Path.Split("\\");
                string searchName = Anitomy.AnitomyHelper.ExtractAnimeTitle(
                    MyAnimelistSearchHelper.PreprocessTitle(splitPath[splitPath.Length - 2])
                );
                int seasonNumber = int.Parse(Anitomy.AnitomyHelper.ExtractSeasonNumber(splitPath[splitPath.Length - 1]));
                Anime anime = (await _jikan.SearchAnimeAsync(searchName, cancellationToken)).Data.Where(a => !a.Type.Equals(AnimeType.Movie.ToString())).FirstOrDefault();
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

                if (anime != null)
                {
                    media = GetAnimeInfo(anime.MalId.Value, cancellationToken).Result;
                }
            }

            if (media.anime != null)
            {
                result.HasMetadata = true;
                result.Item = media.ToSeason(info.IndexNumber.Value);
                result.People = media.GetPeopleInfo();
                result.Provider = ProviderNames.MyAnimeList;
            }

            return result;
        }

        private async Task<Media> GetAnimeInfo(long aid, CancellationToken cancellationToken)
        {
            Media media = new Media();
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
                MediaSearchResult aid_result = new MediaSearchResult();
                aid_result.anime = (await _jikan.GetAnimeAsync(aid, cancellationToken).ConfigureAwait(false)).Data;
                if (aid_result.anime != null)
                {
                    results.Add(aid_result.ToSearchResult());
                }
            }

            if (!string.IsNullOrEmpty(searchInfo.Name))
            {
                ICollection<MediaSearchResult> animeList = (ICollection<MediaSearchResult>)(await _jikan.SearchAnimeAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false)).Data;
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
