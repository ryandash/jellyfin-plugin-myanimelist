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
            var config = Plugin.Instance.Configuration;

            var malId = info.SeasonProviderIds.GetOrDefault(ProviderNames.MyAnimeList);

            if (!string.IsNullOrEmpty(malId) && info.IndexNumber.HasValue)
            {
                _log.LogInformation("Populating Episode metadata for: {malId}", malId);
                try
                {
                    episode = await GetAnimeEpisodeInfo(long.Parse(malId), info.IndexNumber.Value, cancellationToken);
                }
                catch (JikanDotNet.Exceptions.JikanRequestException ex) when (ex.Message.Contains("Status code: NotFound"))
                {
                    _log.LogDebug("Episode not found for MAL ID: {MalId}, Episode: {EpisodeNumber}", malId, info.IndexNumber.Value);
                }
            }
            else
            {
                int seasonNumber = 1;
                if (info.Path == null)
                {
                    return result;
                }
                string[] splitPath = info.Path.Split("\\");
                string part1 = splitPath[^1];
                string searchName = part1.Contains("season", StringComparison.OrdinalIgnoreCase)
                    ? MyAnimelistSearchHelper.PreprocessTitle(splitPath[^2])
                    : MyAnimelistSearchHelper.PreprocessTitle(part1);
                _log.LogInformation("Populating Episode metadata for: {Name}", searchName);
                if (part1.Contains("season", StringComparison.OrdinalIgnoreCase))
                {
                    int season;
                    if (int.TryParse(Anitomy.AnitomyHelper.ExtractSeasonNumber(part1), out season))
                    {
                        seasonNumber = season;
                    }
                }

                var anime = (await _jikan.SearchAnimeAsync(searchName, cancellationToken))?.Data
                    .FirstOrDefault(a => !a.Type.Equals(AnimeType.Movie.ToString()));

                if (anime != null && anime.MalId.HasValue)
                {
                    long aid = anime.MalId.Value;

                    for (int i = 1; i < seasonNumber; i++)
                    {
                        var relation = (await _jikan.GetAnimeRelationsAsync(aid, cancellationToken))?.Data
                            .FirstOrDefault(r => r.Relation.Equals("Sequel", StringComparison.OrdinalIgnoreCase));

                        if (relation == null) break;

                        aid = relation.Entry.FirstOrDefault()?.MalId ?? aid;
                        anime = (await _jikan.GetAnimeAsync(aid, cancellationToken))?.Data;

                        if (anime?.Titles.Any(t => t.Title.Contains("part ", StringComparison.OrdinalIgnoreCase)) == true)
                        {
                            seasonNumber++;
                        }
                    }

                    if (info.IndexNumber.HasValue)
                    {
                        try
                        {
                            episode = await GetAnimeEpisodeInfo(aid, info.IndexNumber.Value, cancellationToken);
                        }
                        catch (JikanDotNet.Exceptions.JikanRequestException ex) when (ex.Message.Contains("Status code: NotFound"))
                        {
                            _log.LogDebug("Episode not found for Anime ID: {Aid}, Episode: {EpisodeNumber}", aid, info.IndexNumber.Value);
                        }
                    }
                }
            }

            if (episode != null && episode?.episode != null)
            {
                result.HasMetadata = true;
                result.Item = episode.ToEpisode();
                result.Provider = ProviderNames.MyAnimeList;
            }

            return result;
        }

        private async Task<EpisodeSearchResult> GetAnimeEpisodeInfo(long aid, int episodeId, CancellationToken cancellationToken)
        {
            try
            {
                return new EpisodeSearchResult
                {
                    episode = (await _jikan.GetAnimeEpisodeAsync(aid, episodeId, cancellationToken)).Data
                };
            }
            catch (JikanDotNet.Exceptions.JikanRequestException ex)
            {
                _log.LogDebug(ex, "Failed to fetch episode {EpisodeId} for Anime ID {Aid}", episodeId, aid);
                return new EpisodeSearchResult
                {
                };
            }
        }


        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            var malId = searchInfo.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList);
            if (!string.IsNullOrEmpty(malId))
            {
                var aid = long.Parse(malId);
                var episode = (await _jikan.GetAnimeEpisodeAsync(aid, searchInfo.IndexNumber.Value)).Data;

                if (episode != null)
                {
                    results.Add(new EpisodeSearchResult { episode = episode }.ToSearchResult());
                }
            }

            if (!string.IsNullOrEmpty(searchInfo.Name))
            {
                var animeList = (await _jikan.SearchAnimeAsync(searchInfo.Name, cancellationToken)).Data;

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
