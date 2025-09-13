using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using JikanDotNet;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class MyAnimeListSearchHelper
    {
        public async Task<AnimeCacheDto> GetAnimeAsync(ILogger _log, ItemLookupInfo info, CancellationToken cancellationToken)
        {
            string malId = info switch
            {
                EpisodeInfo episodeInfo => episodeInfo.SeasonProviderIds.GetOrDefault(ProviderNames.MyAnimeList),
                _ => info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList)
            };

            var config = Plugin.Instance.Configuration;
            bool enableDebug = config.EnableDebug;
            if (enableDebug) _log.LogInformation("Original malID: {malID}", malId);
            if (!string.IsNullOrEmpty(malId))
            {
                if (!config.IgnoreMetadata || info is EpisodeInfo)
                {
                    return (await JikanSingleton.GetAnimeAsync(long.Parse(malId), cancellationToken).ConfigureAwait(false));
                }
                else
                {
                    if (enableDebug) _log.LogInformation("Ignored malID: {malID}", malId);
                }
            }

            if (enableDebug) _log.LogInformation("Original path: {path}", info.Path);
            string searchName = FilterName(GetSearchName(info));
            if (enableDebug) _log.LogInformation("Original name: {name}", searchName);
            long? malIdFromName = await GetBestAnimeID(_log, searchName, info is MovieInfo, config.IgnoreBestAttempt, cancellationToken);
            if (!malIdFromName.HasValue)
            {
                if (enableDebug) _log.LogError("Could not find MalID for: {searchName}", searchName);
                return null;
            }

            if (enableDebug) _log.LogInformation("Found MalID: {malIdFromName}", malIdFromName.Value);
            return info switch
            {
                MovieInfo => (await JikanSingleton.GetAnimeAsync(malIdFromName.Value, cancellationToken).ConfigureAwait(false)),
                _ => await GetCurrentAnimeSeasonAsync(_log, enableDebug, malIdFromName.Value, info.IndexNumber ?? 1, cancellationToken)
            };
        }

        private string GetSearchName(ItemLookupInfo info)
        {
            string[] splitPath = info.Path.Split(Path.DirectorySeparatorChar);
            int index = splitPath.Length - 1;

            switch (info)
            {
                case EpisodeInfo or MovieInfo:
                    while (index > 0)
                    {
                        string folder = splitPath[index - 1];
                        if (!folder.Contains("season", StringComparison.OrdinalIgnoreCase) &&
                            !folder.Contains("special", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                        index--;
                    }
                    return splitPath[Math.Max(0, index - 1)];

                case SeasonInfo:
                    return splitPath[index].Contains("season", StringComparison.OrdinalIgnoreCase) ||
                           splitPath[index].Contains("special", StringComparison.OrdinalIgnoreCase)
                        ? splitPath[Math.Max(0, index - 1)]
                        : splitPath[index];

                case SeriesInfo:
                    return splitPath[index];

                default:
                    return info.Name;
            }
        }

        private static readonly Regex SeasonRegex = new Regex(@"(\s|\.)S[0-9]{1,2}", RegexOptions.Compiled);
        private static readonly Regex AltNameRegex = new Regex(@"\s*~(\w|[0-9]|\s)+~", RegexOptions.Compiled);
        private static readonly Regex NativeNameRegex = new Regex(@"\((\w|[0-9]|\s)+\)$", RegexOptions.Compiled);
        private static readonly Regex AmpersandRegex = new Regex(@"\s?&\s?", RegexOptions.Compiled);
        private static readonly Regex HashRegex = new Regex(@"#", RegexOptions.Compiled);
        private static readonly Regex JellyfinFolderFormatRegex = new Regex(@"\([0-9]{4}\)\s*\[(\w|[0-9]|-)+\]$", RegexOptions.Compiled);

        private string FilterName(string searchName)
        {
            searchName = SeasonRegex.Replace(searchName, string.Empty);               // Remove season designation
            searchName = AltNameRegex.Replace(searchName, string.Empty);              // Remove ALT NAME
            searchName = NativeNameRegex.Replace(searchName, string.Empty);           // Remove native name
            searchName = AmpersandRegex.Replace(searchName, " and ");                 // Replace "&" with "and"
            searchName = HashRegex.Replace(searchName, " ");                          // Replace "#" with space
            searchName = JellyfinFolderFormatRegex.Replace(searchName, string.Empty); // Truncate Jellyfin folder format

            return searchName.Trim();
        }

        private static readonly Regex NormalizeRegex = new Regex("[:.!]", RegexOptions.Compiled);

        private async Task<long?> GetBestAnimeID(
    ILogger _log, string searchTerm, bool isMovie, bool ignoreBestAttempt, CancellationToken cancellationToken)
        {
            var searchResults = await JikanSingleton.SearchAnimeAsync(searchTerm, cancellationToken);
            string normalizedSearch = NormalizeRegex.Replace(searchTerm, string.Empty).ToLowerInvariant();

            long? bestBackupMalId = null;
            int bestBackupSimilarity = -1;

            foreach (var anime in searchResults)
            {
                bool isCorrectType = isMovie
                    ? string.Equals(anime.Type, "Movie", StringComparison.OrdinalIgnoreCase)
                    : !string.Equals(anime.Type, "Movie", StringComparison.OrdinalIgnoreCase);

                if (!isCorrectType)
                    continue;

                foreach (var titleObj in anime.Titles)
                {
                    string title = titleObj.Title.ToLowerInvariant();

                    // Case 1: direct similarity check
                    int similarity = FuzzierSharp.Fuzz.Ratio(NormalizeRegex.Replace(title, string.Empty), normalizedSearch);
                    if (similarity >= 95)
                        return anime.MalId;

                    // Case 2: if title contains ':', compare first part only
                    if (title.Contains(':'))
                    {
                        string firstPart = title[..title.IndexOf(':')].Trim();
                        if (!string.IsNullOrEmpty(firstPart))
                        {
                            string normalizedFirstPart = NormalizeRegex.Replace(firstPart, string.Empty);
                            int partSimilarity = FuzzierSharp.Fuzz.Ratio(normalizedFirstPart, normalizedSearch);
                            if (partSimilarity >= 95)
                                return anime.MalId;

                            if (partSimilarity > bestBackupSimilarity)
                            {
                                bestBackupMalId = anime.MalId;
                                bestBackupSimilarity = partSimilarity;
                                continue;
                            }
                        }
                    }

                    // Case 3: if title contains the searchTerm as substring
                    if (title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                        similarity > bestBackupSimilarity)
                    {
                        bestBackupMalId = anime.MalId;
                        bestBackupSimilarity = similarity;
                    }
                }
            }

            if (bestBackupMalId.HasValue && !ignoreBestAttempt)
                return bestBackupMalId;

            return ignoreBestAttempt
                ? null
                : await MyAnimeListApi.GetBestAttemptId(normalizedSearch, isMovie, cancellationToken);
        }

        private async Task<AnimeCacheDto> GetCurrentAnimeSeasonAsync(
    ILogger _log, bool enableDebug,
    long malId, int seasonNumber, CancellationToken cancellationToken)
        {
            async Task<long?> GetRelatedAnimeIdAsync(long id, string relationType)
            {
                var relations = (await JikanSingleton.GetAnimeRelationsAsync(id, cancellationToken).ConfigureAwait(false))
                                ?.Data ?? new List<RelatedEntry>();

                return relations
                    .FirstOrDefault(r => string.Equals(r.Relation, relationType, StringComparison.OrdinalIgnoreCase))
                    ?.Entry?.FirstOrDefault()?.MalId;
            }

            var anime = (await JikanSingleton.GetAnimeAsync(malId, cancellationToken).ConfigureAwait(false));

            if (anime == null)
                return null;

            if (anime.Titles.Any(t =>
                    t.Title.Contains("2nd", StringComparison.OrdinalIgnoreCase) ||
                    t.Title.Contains("Season 2", StringComparison.OrdinalIgnoreCase)))
            {
                var prequelId = await GetRelatedAnimeIdAsync(malId, "Prequel");
                if (prequelId != null)
                {
                    malId = prequelId.Value;
                    anime = (await JikanSingleton.GetAnimeAsync(malId, cancellationToken).ConfigureAwait(false));
                }
            }

            if (anime.Episodes == 1)
            {
                var sequelId = await GetRelatedAnimeIdAsync(malId, "Sequel");
                if (sequelId != null)
                {
                    malId = sequelId.Value;
                    anime = (await JikanSingleton.GetAnimeAsync(malId, cancellationToken).ConfigureAwait(false));
                }
            }

            for (int currentSeason = 1; currentSeason < seasonNumber; currentSeason++)
            {
                var sequelId = await GetRelatedAnimeIdAsync(malId, "Sequel");
                if (sequelId == null) break;

                malId = sequelId.Value;
                anime = (await JikanSingleton.GetAnimeAsync(malId, cancellationToken).ConfigureAwait(false));

                if (anime == null) break;

                if (anime.Titles.Any(t => t.Title.Contains("part ", StringComparison.OrdinalIgnoreCase)) ||
                    anime.Episodes == 1 ||
                    !(string.Equals(anime.Type, "TV", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(anime.Type, "ONA", StringComparison.OrdinalIgnoreCase)))
                {
                    currentSeason--; // skip this entry
                }
            }

            return anime;
        }
    }
}
