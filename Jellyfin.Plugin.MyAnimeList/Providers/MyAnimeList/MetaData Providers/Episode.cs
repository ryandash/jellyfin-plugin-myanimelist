using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.Helpers;
using JikanDotNet.Exceptions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.IdMappings;
using EpisodeInfo = MediaBrowser.Controller.Providers.EpisodeInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class Episode : Base<MediaBrowser.Controller.Entities.TV.Episode, EpisodeInfo>
    {
        private readonly IdMappings _idMapping;
        private static PluginConfiguration _config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public Episode(ILogger<Episode> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory) : base(logger, libraryManager, httpClientFactory)
        {
            _idMapping = new IdMappings(httpClientFactory);
        }
        public override async Task<MetadataResult<MediaBrowser.Controller.Entities.TV.Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<MediaBrowser.Controller.Entities.TV.Episode>
            {
                QueriedById = true
            };

            if (info.Path is null || !info.IndexNumber.HasValue)
            {
                return result;
            }

            var enableDebug = _config.EnableDebug;

            EpisodeCacheDto episodeData = null;
            AnimeObject anime = null;
            int seasonnumber = info.ParentIndexNumber ?? 1;

            if (_config.UseExternalIDs && info.ProviderIds.TryGetValue("Tvdb", out var tvdbid) && seasonnumber == 0)
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
                            _log.LogError("Episode data null for MAL ID {MalId}, episode {EpisodeNumber}. Episode data: {EpisodeData}",
                                malId,
                                epResult.Episode.Value,
                                episodeData == null ? "null" : $"Episode={episodeData.EpisodeNumber}, Title={episodeData.Title}, Url={episodeData.Url}, HasFullDetails={episodeData.HasFullDetails}");
                        }
                    }
                    else
                    {
                        anime = new AnimeObject
                        {
                            anime = await JikanAPI.GetAnimeFullAsync(malId, cancellationToken).ConfigureAwait(false)
                        };
                        episodeData = anime.toEpisodeData();
                    }
                }
            }

            if (episodeData is null)
            {
                if (_config.ExcludeSpecials && seasonnumber == 0)
                {
                    return result;
                }

                anime = new AnimeObject
                {
                    anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken, false).ConfigureAwait(false)
                };

                if (anime?.anime is null || anime.anime?.MalId is null)
                {
                    return result;
                }

                (var episodeNumber, anime.anime) = await RelationsResolver.GetSeasonEpisodeNumberAsync(
                    _log,
                    info.IndexNumber.Value,
                    seasonnumber,
                    anime.anime,
                    cancellationToken
                ).ConfigureAwait(false);

                if (anime.anime is null)
                {
                    return result;
                }

                var malId = anime.anime.MalId.GetValueOrDefault();

                try
                {
                    episodeData = (anime.anime.Episodes == 1)
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

            var episodeResult = new EpisodeSearchResult { episode = episodeData };

            return new MetadataResult<MediaBrowser.Controller.Entities.TV.Episode>
            {
                HasMetadata = true,
                Item = episodeResult.ToEpisode(info, anime?.anime?.Episodes?.ToString().Length ?? 4),
                Provider = Name
            }; ;
        }

        protected override MediaBrowser.Controller.Entities.TV.Episode ConvertToItem(AnimeObject media, ItemLookupInfo info)
        {
            throw new NotImplementedException();
        }
    }
}
