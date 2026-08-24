using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.Helpers;
using JikanDotNet.Exceptions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.IdMappings;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class EpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>
    {
        protected readonly ILogger _log;
        protected readonly SearchHelper _searchHelper;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IdMappings _idMapping;

        protected static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public string Name => ProviderNames.MyAnimeList;

        protected EpisodeProvider(ILogger logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory)
        {
            _idMapping = new IdMappings(httpClientFactory);
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>
            {
                HasMetadata = false
            };

            if (info.Path is null || !info.IndexNumber.HasValue)
            {
                return result;
            }

            var enableDebug = Config.EnableDebug;

            EpisodeCacheDto episodeData = null;
            AnimeObject anime = null;
            int seasonnumber = info.ParentIndexNumber ?? 1;

            if (Config.UseExternalIDs && info.ProviderIds.TryGetValue("Tvdb", out var tvdbid) && seasonnumber == 0)
            {
                if (enableDebug) _log.LogInformation("Found TVDB ID {TvdbId}", tvdbid);

                AnimeEpisodeMapping epResult = await _idMapping.GetAnimeEpisodeMappingAsync(_log, tvdbid, cancellationToken).ConfigureAwait(false);

                if (epResult?.MalId is long malId)
                {
                    if (enableDebug) _log.LogInformation("MalID: {MalId} Episode: {Episode}", malId, epResult.Episode);

                    if (epResult.Episode.HasValue)
                    {
                        episodeData = await JikanAPI.GetAnimeEpisodeAsync(malId, epResult.Episode.Value, cancellationToken).ConfigureAwait(false);
                        if (episodeData?.Url is null)
                        {
                            _log.LogInformation("Episode data null for MAL ID {MalId}, episode {EpisodeNumber}", malId, epResult.Episode.Value);
                        }
                    }
                    else
                    {
                        anime = new AnimeObject
                        {
                            Anime = await JikanAPI.GetAnimeFullAsync(malId, cancellationToken).ConfigureAwait(false)
                        };
                        episodeData = anime.toEpisodeData();
                    }
                }
            }

            if (episodeData is null)
            {
                if (Config.ExcludeSpecials && seasonnumber == 0)
                {
                    return result;
                }

                anime = new AnimeObject
                {
                    Anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken, false).ConfigureAwait(false)
                };

                if (anime?.Anime is null || anime.Anime?.MalId is null)
                {
                    return result;
                }

                (var episodeNumber, anime.Anime) = await RelationsResolver.GetSeasonEpisodeNumberAsync(
                    _log,
                    info.IndexNumber.Value,
                    seasonnumber,
                    anime.Anime,
                    cancellationToken
                ).ConfigureAwait(false);

                if (anime.Anime is null)
                {
                    return result;
                }

                var malId = anime.Anime.MalId.GetValueOrDefault();

                try
                {
                    episodeData = (anime.Anime.Episodes == 1)
                        ? anime.toEpisodeData()
                        : await JikanAPI.GetAnimeEpisodeAsync(malId, episodeNumber, cancellationToken).ConfigureAwait(false);
                }
                catch (JikanRequestException ex) when (ex.ApiError?.Status is not (HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable))
                {
                    _log.LogError("Failed to get episode data for MAL ID {MalId}, episode {EpisodeNumber} Exception: {ex}", malId, episodeNumber, ex);
                }
                catch (JikanRequestException)
                {
                }
                catch (HttpRequestException)
                {
                }

                if (string.IsNullOrWhiteSpace(episodeData?.Url))
                {
                    _log.LogError("Episode data null for MAL ID {MalId}, episode {EpisodeNumber}. Episode data: {EpisodeData}",
                        malId,
                        episodeNumber,
                        episodeData == null ? "null" : $"Episode={episodeData.EpisodeNumber}, Title={episodeData.Title}, Url={episodeData.Url}, HasFullDetails={episodeData.HasFullDetails}");
                }
            }

            if (episodeData == null)
            {
                return result;
            }

            var episodeResult = new EpisodeSearchResult { Episode = episodeData };

            return new MetadataResult<Episode>
            {
                HasMetadata = true,
                Item = episodeResult.ToEpisode(info),
                Provider = Name
            }; ;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo searchInfo, CancellationToken cancellationToken)
        {
            var anime = await _searchHelper.GetAnimeAsync(_log, searchInfo, cancellationToken, true).ConfigureAwait(false);

            if (anime is null)
            {
                return [];
            }

            return
            [
                new AnimeSearchResult
            {
                Anime = anime
            }.ToSearchResult()
            ];
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(ProviderNames.MyAnimeList).GetAsync(url, cancellationToken);
        }
    }
}
