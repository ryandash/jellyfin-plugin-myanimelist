using JikanDotNet;
using JikanDotNet.Exceptions;
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
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using EpisodeInfo = MediaBrowser.Controller.Providers.EpisodeInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
    {
        private readonly ILogger<MyAnimeListEpisodeProvider> _log;
        private readonly Jikan _jikan;

        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListEpisodeProvider(ILogger<MyAnimeListEpisodeProvider> logger)
        {
            _log = logger;
            _jikan = NewJikan._jikan;
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>();
            var episode = new EpisodeSearchResult();
            var malId = info.SeasonProviderIds.GetOrDefault(ProviderNames.MyAnimeList);
            var malIDPartSeason = info.SeasonProviderIds.GetOrDefault(ProviderNames.MyAnimeListSeason);

            var anime = await GetAnimeInfoAsync(malId, info, cancellationToken).ConfigureAwait(false);
            if (anime == null) return result;
            (var episodeNumber, anime.MalId) = await GetEpisodeNumberAsync(info, anime, malIDPartSeason, cancellationToken).ConfigureAwait(false);
            try
            {
                episode.episode = (await _jikan.GetAnimeEpisodeAsync(anime.MalId.Value, episodeNumber, cancellationToken).ConfigureAwait(false)).Data;
            }
            catch (JikanRequestException)
            {
                // It is normal for some episode data to not exist on myanimelist
            }

            if (episode == null || episode.episode == null)
            {
                return result;
            }

            anime = (await _jikan.GetAnimeAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false)).Data;

            result.HasMetadata = true;
            result.Item = episode.ToEpisode(anime.Episodes?.ToString().Length ?? 4);
            result.Provider = ProviderNames.MyAnimeList;
            return result;
        }

        private async Task<JikanDotNet.Anime> GetAnimeInfoAsync(string malId, EpisodeInfo info, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(malId))
            {
                var aid = long.Parse(malId);
                _log.LogInformation("Populating Episode Anime info for: {malId}", aid);
                return (await _jikan.GetAnimeAsync(aid, cancellationToken).ConfigureAwait(false)).Data;
            }

            if (info.Path == null || !info.ParentIndexNumber.HasValue) return null;
            string[] splitPath = info.Path.Split("\\");
            string part1 = splitPath[^2];
            string searchName = part1.Contains("season", StringComparison.OrdinalIgnoreCase)
                ? MyAnimeListSearchHelper.PreprocessTitle(splitPath[^3])
                : MyAnimeListSearchHelper.PreprocessTitle(part1);
            _log.LogInformation("Populating Episode Anime info for: {Name}", searchName);

            var anime = (await _jikan.SearchAnimeAsync(searchName, cancellationToken).ConfigureAwait(false))?.Data
                        .FirstOrDefault(a => a.Type == null || !a.Type.Equals(AnimeType.Movie.ToString()));
            return await GetAnimeBySeasonAsync(anime, info.ParentIndexNumber.Value, cancellationToken).ConfigureAwait(false);
        }

        private async Task<JikanDotNet.Anime> GetAnimeBySeasonAsync(JikanDotNet.Anime anime, int seasonNumber, CancellationToken cancellationToken)
        {
            if (anime == null) return null;

            long aid = anime.MalId.Value;
            for (int i = 1; i < seasonNumber; i++)
            {
                var relation = (await _jikan.GetAnimeRelationsAsync(aid, cancellationToken).ConfigureAwait(false))?.Data
                               .FirstOrDefault(r => r.Relation.Equals("Sequel", StringComparison.OrdinalIgnoreCase));
                if (relation == null) break;

                var malurl = relation.Entry.FirstOrDefault();
                if (malurl == null) break;

                aid = malurl.MalId;
                anime = (await _jikan.GetAnimeAsync(aid, cancellationToken).ConfigureAwait(false)).Data;

                if (anime.Titles.Any(t => t.Title.Contains("part ", StringComparison.OrdinalIgnoreCase)) ||
    !(anime.Type.Equals("TV", StringComparison.OrdinalIgnoreCase) || anime.Type.Equals("ONA", StringComparison.OrdinalIgnoreCase)))
                {
                    seasonNumber++;
                }
            }

            return anime;
        }

        private async Task<(int episodeNumber, long? malID)> GetEpisodeNumberAsync(
    EpisodeInfo info, JikanDotNet.Anime anime, string malIDPartSeason, CancellationToken cancellationToken)
        {
            int episodeNumber = info.IndexNumber.Value;

            if (anime.Episodes == null)
            {
                _log.LogError("Jikan did not find the correct anime. It found: {name} {id}", anime.Titles.First().Title, anime.MalId.ToString());
            }

            if (episodeNumber <= anime.Episodes)
            {
                return (episodeNumber, anime.MalId);
            }

            int tempEpisodeNumber = (int)(episodeNumber - anime.Episodes);

            if (!string.IsNullOrEmpty(malIDPartSeason))
            {
                return (tempEpisodeNumber, long.Parse(malIDPartSeason));
            }

            var sequel = (await _jikan.GetAnimeRelationsAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false))?.Data
                         .FirstOrDefault(r => r.Relation.Equals("Sequel", StringComparison.OrdinalIgnoreCase))?.Entry.FirstOrDefault();

            if (sequel != null)
            {
                info.SeasonProviderIds.Add(ProviderNames.MyAnimeListSeason, sequel.MalId.ToString());
                return (tempEpisodeNumber, sequel.MalId);
            }

            return (episodeNumber, anime.MalId);
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            var malId = searchInfo.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList);
            if (!string.IsNullOrEmpty(malId))
            {
                var aid = long.Parse(malId);
                var episode = (await _jikan.GetAnimeEpisodeAsync(aid, searchInfo.IndexNumber.Value, cancellationToken).ConfigureAwait(false)).Data;

                if (episode != null)
                {
                    results.Add(new EpisodeSearchResult { episode = episode }.ToSearchResult());
                }
            }

            if (!string.IsNullOrEmpty(searchInfo.Name))
            {
                var animeList = (await _jikan.SearchAnimeAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false)).Data;

                if (animeList != null)
                {
                    results.AddRange(animeList.Select(anime => new AnimeSearchResult { anime = anime }.ToSearchResult()));
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
