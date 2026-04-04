using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using JikanDotNet.Exceptions;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using EpisodeInfo = MediaBrowser.Controller.Providers.EpisodeInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListEpisodeProvider : MyAnimeListBaseProvider<Episode, EpisodeInfo>
    {
        private readonly IdMappings _idMapping;
        public MyAnimeListEpisodeProvider(ILogger<MyAnimeListEpisodeProvider> logger, ILibraryManager libraryManager) : base(logger, libraryManager)
        {
            _idMapping = new IdMappings();
        }
        public override async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Episode>();

            if (info.Path == null || !info.IndexNumber.HasValue)
                return result;

            var config = Plugin.Instance.Configuration;
            var enableDebug = config.EnableDebug;

            EpisodeCacheDto episodeData = null;
            AnimeObject anime = null;

            if (config.UseExternalIDs && info is EpisodeInfo episodeInfo && episodeInfo.ProviderIds.TryGetValue("Tvdb", out var tvdbid))
            {
                if (enableDebug) _log.LogInformation("Found TVDB ID {TvdbId}", tvdbid);

                var epResult = await _idMapping.GetAnimeEpisodeMappingAsync(_log, tvdbid, cancellationToken).ConfigureAwait(false);

                if (epResult?.MalId is long malId)
                {
                    if (enableDebug) _log.LogInformation("MalID: {MalId} Season: {Season} Episode: {Episode}",
                        malId, epResult.Season, epResult.Episode);

                    if (epResult.Episode.HasValue)
                    {
                        episodeData = await JikanAPI.GetAnimeEpisodeAsync(malId, epResult.Episode!.Value, cancellationToken).ConfigureAwait(false);

                    }
                    else
                    {
                        anime = new AnimeObject
                        {
                            anime = await _searchHelper.GetCurrentAnimeSeasonAsync(_log, malId, info.ParentIndexNumber ?? epResult.Season.GetValueOrDefault(1), cancellationToken).ConfigureAwait(false)
                        };
                        episodeData = anime.toEpisodeData();
                    }
                }
            }

            if (episodeData == null)
            {
                anime = new AnimeObject
                {
                    anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken, false).ConfigureAwait(false)
                };

                if (anime?.anime == null || anime.anime?.MalId == null)
                    return result;

                var (episodeNumber, updatedAnime) = await GetSeasonEpisodeNumberAsync(
                    _log,
                    info.IndexNumber.Value,
                    info.ParentIndexNumber!.Value,
                    anime.anime,
                    cancellationToken
                ).ConfigureAwait(false);

                anime.anime = updatedAnime;

                if (anime.anime == null)
                    return result;

                var malId = anime.anime.MalId.GetValueOrDefault();

                try
                {
                    episodeData = anime.anime.Episodes != 1
                        ? await JikanAPI.GetAnimeEpisodeAsync(malId, episodeNumber, cancellationToken).ConfigureAwait(false)
                        : anime.toEpisodeData();
                }
                catch (JikanRequestException)
                {
                    _log.LogInformation("No episode data for MAL ID {MalId}, episode {EpisodeNumber}", malId, episodeNumber);
                }
            }

            if (episodeData == null)
                return result;

            var episodeResult = new EpisodeSearchResult { episode = episodeData };

            result.HasMetadata = true;
            result.Item = episodeResult.ToEpisode(anime!.anime?.Episodes?.ToString().Length ?? 4);
            result.Item.IndexNumber = info.IndexNumber;
            result.Provider = ProviderNames.MyAnimeList;

            return result;
        }

        protected override Episode ConvertToItem(AnimeObject media)
        {
            throw new NotImplementedException();
        }

        private async Task<(int episodeNumber, AnimeFullCacheDto anime)> GetSeasonEpisodeNumberAsync(
            ILogger _log, int episodeNumber, int seasonNumber,
            AnimeFullCacheDto anime, CancellationToken cancellationToken)
        {
            if (!anime.Episodes.HasValue)
            {
                return (episodeNumber, anime);
            }

            var relations = anime.Relations ?? (await JikanAPI.GetAnimeFullAsync(anime.MalId.Value, cancellationToken, true).ConfigureAwait(false))
                                                 ?.Relations ?? new List<RelatedEntryDto>();

            async Task<AnimeFullCacheDto> GetRelatedAnimeAsync(string relationType)
            {
                var relation = relations.FirstOrDefault(r =>
                    r.Relation.Equals(relationType, StringComparison.OrdinalIgnoreCase))
                    ?.Entry.FirstOrDefault();

                return relation.HasValue
                    ? await JikanAPI.GetAnimeFullAsync(relation.Value, cancellationToken, true).ConfigureAwait(false)
                    : null;
            }

            while (anime.Episodes.HasValue && anime.Episodes.Value > 0 && episodeNumber > anime.Episodes.Value)
            {
                var sequelAnime = await GetRelatedAnimeAsync("Sequel").ConfigureAwait(false);
                if (sequelAnime == null || (sequelAnime.Episodes.HasValue && sequelAnime.Episodes.Value == 0))
                    break;

                episodeNumber -= anime.Episodes.Value;
                anime = sequelAnime;
                relations = anime.Relations ?? new List<RelatedEntryDto>();

                if (!sequelAnime.Episodes.HasValue)
                    break;
            }

            if (episodeNumber == 0)
            {
                var prequelAnime = await GetRelatedAnimeAsync("Prequel").ConfigureAwait(false);
                if (prequelAnime != null && (!prequelAnime.Episodes.HasValue || prequelAnime.Episodes.Value > 0))
                {
                    anime = prequelAnime;
                    if (prequelAnime.Episodes.HasValue)
                        episodeNumber += prequelAnime.Episodes.Value;
                }
                return (episodeNumber, anime);
            }

            if (seasonNumber == 0 && !Plugin.Instance.Configuration.ExcludeSpecials)
            {
                var sideStories = relations.FirstOrDefault(r =>
                    r.Relation.Equals("Side Story", StringComparison.OrdinalIgnoreCase))?.Entry;

                if (sideStories != null && episodeNumber > 0)
                {
                    var tempEpisodeNumber = episodeNumber;
                    foreach (var sideStory in sideStories)
                    {
                        var sideStoryAnime = await JikanAPI.GetAnimeFullAsync(sideStory, cancellationToken)
                            .ConfigureAwait(false);

                        var numEpisodes = sideStoryAnime?.Episodes;
                        if (!numEpisodes.HasValue) break;

                        if (tempEpisodeNumber > numEpisodes.Value)
                        {
                            tempEpisodeNumber -= numEpisodes.Value;
                        }
                        else
                        {
                            return (tempEpisodeNumber, sideStoryAnime);
                        }
                    }
                }

                return (0, null);
            }

            return (episodeNumber, anime);
        }
    }
}
