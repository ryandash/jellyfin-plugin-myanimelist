using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using MediaBrowser.Controller.Library;
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
        private readonly string[] _libraryRoots;

        public MyAnimeListSearchHelper(ILibraryManager libraryManager)
        {
            _libraryRoots = libraryManager
                .GetVirtualFolders()
                .SelectMany(v => v.Locations)
                .Where(l => !string.IsNullOrEmpty(l))
                .Select(l => l.TrimEnd(Path.DirectorySeparatorChar))
                .OrderByDescending(l => l.Length)
                .ToArray();
        }

        public async Task<AnimeCacheDto> GetAnimeAsync(ILogger _log, ItemLookupInfo info, CancellationToken cancellationToken, bool SearchResult)
        {
            string malId = info switch
            {
                EpisodeInfo episodeInfo => episodeInfo.SeasonProviderIds.GetOrDefault(ProviderNames.MyAnimeList),
                _ => info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList)
            };

            var config = Plugin.Instance.Configuration;
            bool enableDebug = config.EnableDebug;
            if (!string.IsNullOrEmpty(malId))
            {
                if (!config.IgnoreMetadata || (info is EpisodeInfo && !config.IgnoreEpisodeMetadata) || SearchResult)
                {
                    if (enableDebug) _log.LogInformation("Returned malID: {malID} for type {type}", malId, info.GetType().ToString());
                    return (await JikanSingleton.GetAnimeAsync(long.Parse(malId), cancellationToken).ConfigureAwait(false));
                }
                else
                {
                    if (enableDebug) _log.LogInformation("Ignored malID: {malID}", malId);
                }
            }

            if (enableDebug) _log.LogInformation("Original path: {path}", info.Path);
            if (enableDebug) _log.LogInformation("Original name: {name}", info.Name);
            string searchName = FilterName(GetSearchName(info, _log, enableDebug));
            if (enableDebug) _log.LogInformation("Filtered name: {name}", searchName);
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
                EpisodeInfo => await GetCurrentAnimeSeasonAsync(malIdFromName.Value, info.ParentIndexNumber ?? 1, cancellationToken),
                _ => await GetCurrentAnimeSeasonAsync(malIdFromName.Value, info.IndexNumber ?? 1, cancellationToken)
            };
        }
        private string StripLibraryPath(string itemPath, ILogger log, bool enableDebug)
        {
            if (string.IsNullOrEmpty(itemPath))
                return itemPath;

            foreach (var root in _libraryRoots)
            {
                if (itemPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    if (enableDebug) log.LogInformation("Removed library root '{Root}' from '{Path}'", root, itemPath);

                    int start = root.Length;
                    if (start < itemPath.Length && itemPath[start] == Path.DirectorySeparatorChar)
                        start++;

                    return itemPath[start..];
                }
            }

            return itemPath;
        }

        private string GetSearchName(ItemLookupInfo info, ILogger _log, bool enableDebug)
        {
            if (string.IsNullOrEmpty(info.Path))
                return info.Name;
            string relativePath = StripLibraryPath(info.Path, _log, enableDebug);
            var splitPath = relativePath.Split(Path.DirectorySeparatorChar);
            if (enableDebug) _log.LogInformation($"{splitPath.Length} \"{string.Join("\", \"", splitPath)}\"");
            if (splitPath.Length < 1) return info.Name;
            int index = splitPath.Length - 1;

            string GetFolderForEpisodeOrMovie(string[] path, int idx)
            {
                while (idx > 0)
                {
                    string folder = path[idx - 1];
                    if (!folder.Contains("season", StringComparison.OrdinalIgnoreCase) &&
                        !folder.Contains("special", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    idx--;
                }
                return path[Math.Max(0, idx - 1)];
            }

            switch (info)
            {
                case EpisodeInfo episode:
                    string title = episode.Name.Split(['-', '_']).Last().Trim();

                    return (episode.ParentIndexNumber == 0
                        && !title.Any(char.IsDigit))
                       ? title
                       : GetFolderForEpisodeOrMovie(splitPath, index);

                case MovieInfo:
                    return GetFolderForEpisodeOrMovie(splitPath, index);

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
        private static readonly Regex QuoteMatches = new Regex("\"([^\"]+)\"", RegexOptions.Compiled);

        private async Task<long?> GetBestAnimeID(
    ILogger _log, string searchTerm, bool isMovie, bool ignoreBestAttempt, CancellationToken cancellationToken)
        {
            var searchResults = await JikanSingleton.SearchAnimeAsync(searchTerm, cancellationToken);
            string normalizedSearch = NormalizeRegex.Replace(searchTerm, string.Empty).ToLowerInvariant();

            long? bestBackupMalId = null;
            int bestBackupSimilarity = -1;

            long? bestMalId = null;
            int bestSimilarity = -1;

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
                    if (similarity == 100)
                        return anime.MalId;

                    if (similarity >= 95 && similarity > bestSimilarity)
                    {
                        bestSimilarity = similarity;
                        bestMalId = anime.MalId;
                    }

                    // Case 2: if title contains ':', compare first part only
                    if (title.Contains(':'))
                    {
                        string firstPart = title[..title.IndexOf(':')].Trim();
                        if (!string.IsNullOrEmpty(firstPart))
                        {
                            string normalizedFirstPart = NormalizeRegex.Replace(firstPart, string.Empty);
                            int partSimilarity = FuzzierSharp.Fuzz.Ratio(normalizedFirstPart, normalizedSearch);

                            if (partSimilarity >= 95 && partSimilarity > bestBackupSimilarity)
                            {
                                bestBackupMalId = anime.MalId;
                                bestBackupSimilarity = partSimilarity;
                                continue;
                            }
                        }
                    }

                    // Case 3: if title contains the searchTerm as substring
                    if (title.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) &&
                        similarity > bestBackupSimilarity)
                    {
                        bestBackupMalId = anime.MalId;
                        bestBackupSimilarity = similarity;
                        continue;
                    }

                    // Case 4: if the title contains quoted words (Typically long titles)
                    if (title.Contains('"'))
                    {
                        var quoteMatches = QuoteMatches.Matches(title);
                        foreach (Match match in quoteMatches)
                        {
                            string quotedWord = match.Groups[1].Value.ToLowerInvariant();
                            if (!string.IsNullOrEmpty(quotedWord))
                            {
                                int quotedSimilarity = FuzzierSharp.Fuzz.Ratio(
                                    NormalizeRegex.Replace(quotedWord, string.Empty),
                                    normalizedSearch);

                                if (quotedSimilarity >= 95 && quotedSimilarity > bestBackupSimilarity)
                                {
                                    bestBackupMalId = anime.MalId;
                                    bestBackupSimilarity = quotedSimilarity;
                                    continue;
                                }
                            }
                        }
                    }
                }
            }

            if (bestMalId.HasValue)
                return bestMalId;

            if (bestBackupMalId.HasValue && !ignoreBestAttempt)
                return bestBackupMalId;

            return ignoreBestAttempt
                ? null
                : await MyAnimeListApi.GetBestAttemptId(normalizedSearch, isMovie, cancellationToken);
        }

        public async Task<AnimeCacheDto> GetCurrentAnimeSeasonAsync(long malId, int seasonNumber, CancellationToken cancellationToken)
        {
            async Task<List<long>> GetRelatedAnimeIdsAsync(long id, string relationType)
            {
                var relations = await JikanSingleton.GetAnimeRelationsAsync(id, cancellationToken)
                    .ConfigureAwait(false);

                if (relations == null)
                    return new List<long>();

                return relations
                    .Where(r => r?.Relation?.Equals(relationType, StringComparison.OrdinalIgnoreCase) == true)
                    .SelectMany(r => r.Entry ?? new List<long>())
                    .Where(e => e > 0)
                    .ToList();
            }

            var anime = await JikanSingleton.GetAnimeAsync(malId, cancellationToken).ConfigureAwait(false);
            if (anime == null)
                return null;

            static bool IsSkippable(AnimeCacheDto anime) =>
                anime.Episodes == 1 ||
                anime.Titles?.Any(t => t.Title.Contains("OVA", StringComparison.OrdinalIgnoreCase) ||
                                       t.Title.Contains("Special", StringComparison.OrdinalIgnoreCase) &&
                                       !t.Title.Contains("TV Special", StringComparison.OrdinalIgnoreCase) ||
                                       t.Title.Contains("part ", StringComparison.OrdinalIgnoreCase)) == true ||
                !(anime.Type.Equals("TV", StringComparison.OrdinalIgnoreCase) ||
                  anime.Type.Equals("ONA", StringComparison.OrdinalIgnoreCase) ||
                  anime.Type.Equals("TV Special", StringComparison.OrdinalIgnoreCase));

            async Task<AnimeCacheDto> GetNextValidSequelAsync(long currentMalId)
            {
                var sequelIds = await GetRelatedAnimeIdsAsync(currentMalId, "Sequel");
                foreach (var id in sequelIds)
                {
                    var candidate = await JikanSingleton.GetAnimeAsync(id, cancellationToken);
                    if (candidate != null && !IsSkippable(candidate))
                        return candidate;
                }
                return null;
            }

            if (anime.Episodes == 1)
            {
                var next = await GetNextValidSequelAsync(anime.MalId!.Value);
                if (next != null)
                    anime = next;
            }

            int currentSeason = 1;
            while (currentSeason < seasonNumber)
            {
                var nextSeason = await GetNextValidSequelAsync(anime.MalId!.Value);
                if (nextSeason == null)
                    break;

                anime = nextSeason;
                currentSeason++;
            }

            return anime;
        }

    }
}
