using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using JikanDotNet.Exceptions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using static Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.IdMappings;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using EpisodeInfo = MediaBrowser.Controller.Providers.EpisodeInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListEpisodeProvider : MyAnimeListBaseProvider<Episode, EpisodeInfo>
    {
        private readonly IdMappings _idMapping;
        private static PluginConfiguration _config;

        public MyAnimeListEpisodeProvider(ILogger<MyAnimeListEpisodeProvider> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory) : base(logger, libraryManager, httpClientFactory)
        {
            _idMapping = new IdMappings(httpClientFactory);
            _config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        }
        public override async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>();
            if (info.Path is null || !info.IndexNumber.HasValue)
                return result;

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
                        if (episodeData.Url is null)
                        {
                            _log.LogError("Episode data null for MAL ID {MalId}, episode {EpisodeNumber}", malId, epResult.Episode.Value);
                            return result;
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
                    return result;

                anime = new AnimeObject
                {
                    anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken, false).ConfigureAwait(false)
                };

                if (anime?.anime is null || anime.anime?.MalId is null)
                    return result;

                var (episodeNumber, updatedAnime) = await _searchHelper.GetSeasonEpisodeNumberAsync(
                    _log,
                    info.IndexNumber.Value,
                    seasonnumber,
                    anime.anime,
                    cancellationToken
                ).ConfigureAwait(false);

                anime.anime = updatedAnime;

                if (anime.anime is null)
                    return result;

                var malId = anime.anime.MalId.GetValueOrDefault();

                try
                {
                    episodeData = anime.anime.Episodes != 1
                        ? await JikanAPI.GetAnimeEpisodeAsync(malId, episodeNumber, cancellationToken).ConfigureAwait(false)
                        : anime.toEpisodeData();
                }
                catch (JikanRequestException ex)
                {

                    if (ex.ApiError?.Status is not (HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable))
                    {
                        _log.LogError("Failed to get episode data for MAL ID {MalId}, episode {EpisodeNumber} Exception: {ex}", malId, episodeNumber, ex);
                    }
                }
                if (string.IsNullOrWhiteSpace(episodeData?.Url))
                {
                    _log.LogError("Episode data null for MAL ID {MalId}, episode {EpisodeNumber}", malId, episodeNumber);
                    return result;
                }
            }

            var episodeResult = new EpisodeSearchResult { episode = episodeData };

            result.HasMetadata = true;
            result.Item = episodeResult.ToEpisode(anime!.anime?.Episodes?.ToString().Length ?? 4);
            result.Provider = Name;

            return result;
        }

        protected override Episode ConvertToItem(AnimeObject media)
        {
            throw new NotImplementedException();
        }
    }
}
