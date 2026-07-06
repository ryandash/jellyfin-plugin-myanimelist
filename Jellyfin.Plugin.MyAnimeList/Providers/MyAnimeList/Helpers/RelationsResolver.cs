using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.Helpers
{
    public class RelationsResolver
    {
        private static int GetTypePriority(string type) => type?.ToUpperInvariant() switch
        {
            "TV" => 0,
            "ONA" => 1,
            "TV SPECIAL" => 2,
            "MOVIE" => 3,
            "OVA" => 4,
            "SPECIAL" => 5,
            _ => int.MaxValue
        };

        private static bool IsPartTwoOrLater(string title)
        {
            var idx = title.IndexOf("part ", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return false;

            idx += 5;

            while (idx < title.Length && title[idx] == ' ')
                idx++;

            int start = idx;
            while (idx < title.Length && char.IsDigit(title[idx]))
                idx++;

            if (start == idx)
                return false;

            return int.TryParse(title[start..idx], out var part) && part > 1;
        }

        private static bool IsSpecialTitle(string title) =>
            title.Contains("OVA", StringComparison.OrdinalIgnoreCase) ||
            (title.Contains("Special", StringComparison.OrdinalIgnoreCase) &&
             !title.Contains("TV Special", StringComparison.OrdinalIgnoreCase));

        private static bool IsSkippable(AnimeFullCacheDto anime)
        {
            var titles = anime.Titles ?? [];

            var isPart = titles.Any(t => t.Title is not null && IsPartTwoOrLater(t.Title));

            var isSpecial = titles.Any(t => t.Title is not null && IsSpecialTitle(t.Title));

            var allowedTypes = new[]
            {
                "TV",
                "ONA",
                "TV Special",
                "MOVIE"
            };

            var isWrongType = !allowedTypes.Contains(anime.Type, StringComparer.OrdinalIgnoreCase);

            var isSingleEpisode = anime.Episodes.GetValueOrDefault() == 1 &&
                !string.Equals(anime.Type, "MOVIE", StringComparison.OrdinalIgnoreCase);

            return isPart || isSpecial || isWrongType || isSingleEpisode;
        }

        private static async Task<List<long>> GetRelatedAnimeIdsAsync(long id, string relationType, CancellationToken cancellationToken)
        {
            var animeWithRelations = await JikanAPI.GetAnimeFullAsync(id, cancellationToken, true).ConfigureAwait(false);

            var relations = animeWithRelations?.Relations;
            if (relations is null || relations.Count == 0)
                return new List<long>(0);

            var result = new List<long>();

            foreach (var r in relations)
            {
                if (r?.Relation is null ||
                    !r.Relation.Equals(relationType, StringComparison.OrdinalIgnoreCase))
                    continue;

                var entries = r.Entry;
                if (entries is null)
                    continue;

                foreach (var e in entries)
                {
                    if (e > 0)
                        result.Add(e);
                }
            }

            return result;
        }

        private static async Task<AnimeFullCacheDto> GetFirstSequelAsync(long id, CancellationToken cancellationToken)
        {
            var sequelIds = await GetRelatedAnimeIdsAsync(id, "Sequel", cancellationToken).ConfigureAwait(false);

            AnimeFullCacheDto best = null;
            int bestPriority = int.MaxValue;

            foreach (var sequelId in sequelIds)
            {
                var anime = await JikanAPI
                    .GetAnimeFullAsync(sequelId, cancellationToken)
                    .ConfigureAwait(false);

                if (anime?.Type is null)
                    continue;

                var priority = GetTypePriority(anime.Type);

                if (priority < bestPriority)
                {
                    best = anime;
                    bestPriority = priority;

                    if (priority == 0)
                        return best;
                }
            }

            return best;
        }

        public static async Task<AnimeFullCacheDto> GetCurrentAnimeSeasonAsync(ILogger _log, long malId, int similarityConfidence, int seasonNumber, CancellationToken cancellationToken)
        {
            if (similarityConfidence < 90)
            {
                var visited = new HashSet<long>();

                while (visited.Add(malId))
                {
                    var prequelIds = await GetRelatedAnimeIdsAsync(malId, "Prequel", cancellationToken).ConfigureAwait(false);
                    var prequelId = prequelIds.FirstOrDefault();

                    if (prequelId == default)
                        break;

                    malId = prequelId;
                }
            }

            var anime = await JikanAPI.GetAnimeFullAsync(malId, cancellationToken).ConfigureAwait(false);
            if (anime?.MalId is null)
                return null;

            if (anime.Episodes == 1)
            {
                var next = await GetFirstSequelAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false);
                if (next is not null)
                    anime = next;
            }

            int currentSeason = 1;

            while (currentSeason < seasonNumber)
            {
                var next = await GetFirstSequelAsync(anime.MalId!.Value, cancellationToken).ConfigureAwait(false);
                if (next is null)
                    break;

                anime = next;

                if (IsSkippable(anime)) continue;

                currentSeason++;
            }

            return anime;
        }

        public static async Task<(int episodeNumber, AnimeFullCacheDto anime)> GetSeasonEpisodeNumberAsync(ILogger _log, int episodeNumber, int seasonNumber, AnimeFullCacheDto anime, CancellationToken cancellationToken)
        {
            var isSpecialType = anime.Type.Equals("Special", StringComparison.OrdinalIgnoreCase) ||
                    anime.Type.Equals("TV Special", StringComparison.OrdinalIgnoreCase) ||
                    anime.Type.Equals("OVA", StringComparison.OrdinalIgnoreCase);

            // If season 0 special with a special type then return, if it is a season 1+ and not a special then return, else continue
            if ((seasonNumber == 0 && isSpecialType) || (!anime.Episodes.HasValue && seasonNumber != 0))
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
                if (sequelAnime is null || (sequelAnime.Episodes.HasValue && sequelAnime.Episodes.Value == 0))
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
                if (prequelAnime is not null && (!prequelAnime.Episodes.HasValue || prequelAnime.Episodes.Value > 0))
                {
                    anime = prequelAnime;
                    if (prequelAnime.Episodes.HasValue)
                        episodeNumber += prequelAnime.Episodes.Value;
                }
                return (episodeNumber, anime);
            }

            if (seasonNumber == 0)
            {
                var sideStories = relations.FirstOrDefault(r =>
                    r.Relation.Equals("Side Story", StringComparison.OrdinalIgnoreCase))?.Entry;

                if (sideStories is not null && episodeNumber > 0)
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
