using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AnitomySharp;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using static AnitomySharp.AnitomySharp;
using static Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.MyAnimeListApi;

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

        public async Task<AnimeFullCacheDto> GetAnimeAsync(ILogger _log, ItemLookupInfo info, CancellationToken cancellationToken, bool SearchResult)
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
                    return (await JikanAPI.GetAnimeFullAsync(long.Parse(malId), cancellationToken).ConfigureAwait(false));
                }
                else
                {
                    if (enableDebug) _log.LogInformation("Ignored malID: {malID}", malId);
                }
            }

            if (enableDebug) _log.LogInformation("Original path: {path}", info.Path);
            if (enableDebug) _log.LogInformation("Original name: {name}", info.Name);
            string searchName = GetSearchName(info, _log, enableDebug);
            if (enableDebug) _log.LogInformation("Search name: {name}", searchName);
            long? malIdFromName = await GetBestAnimeID(_log, searchName, info is MovieInfo, config.IgnoreBestAttempt, cancellationToken).ConfigureAwait(false);
            if (!malIdFromName.HasValue)
            {
                if (enableDebug) _log.LogError("Could not find MalID for: {searchName}", searchName);
                return null;
            }

            if (enableDebug) _log.LogInformation("Found MalID: {malIdFromName}", malIdFromName.Value);
            return info switch
            {
                MovieInfo => (await JikanAPI.GetAnimeFullAsync(malIdFromName.Value, cancellationToken).ConfigureAwait(false)),
                EpisodeInfo => await GetCurrentAnimeSeasonAsync(_log, malIdFromName.Value, info.ParentIndexNumber ?? 1, cancellationToken).ConfigureAwait(false),
                _ => await GetCurrentAnimeSeasonAsync(_log, malIdFromName.Value, info.IndexNumber ?? 1, cancellationToken).ConfigureAwait(false)
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
            if (enableDebug) _log.LogInformation($"Split location: {splitPath.Length} \"{string.Join("\", \"", splitPath)}\"");
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

        private static readonly Regex NormalizeRegex = new Regex("[:.!]", RegexOptions.Compiled);

        private static void ExtractTitleAndYear(string searchTerm, out string title, out string year)
        {
            title = null;
            year = null;
            IEnumerable<Element> elements = Parse(searchTerm);

            foreach (var e in elements)
            {
                switch (e.Category)
                {
                    case Element.ElementCategory.ElementAnimeTitle:
                        title ??= e.Value;
                        break;

                    case Element.ElementCategory.ElementAnimeYear:
                        year ??= e.Value;
                        break;
                }

                if (title != null && year != null)
                    return;
            }
        }

        private async Task<long?> GetBestAnimeID(ILogger _log, string searchTerm, bool isMovie, bool ignoreBestAttempt, CancellationToken cancellationToken)
        {
            ExtractTitleAndYear(searchTerm, out string searchTitle, out string year);
            bool hasParsedYear = int.TryParse(year, out int parsedYear);

            var searchResults = await JikanAPI.SearchAnimeAsync(searchTitle, cancellationToken).ConfigureAwait(false);
            string normalizedSearch = NormalizeRegex.Replace(searchTitle, string.Empty).ToLowerInvariant();

            Func<string, bool> mediaTypeCondition = isMovie
                ? mediaType => string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase)
                : mediaType => !string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase);

            long? bestBackupMalId = null;
            int bestBackupSimilarity = -1;

            long? bestMalId = null;
            int bestSimilarity = -1;

            foreach (var anime in searchResults)
            {
                if (!mediaTypeCondition(anime.Type)) continue;

                if (hasParsedYear)
                {
                    int? animeYear = anime.Aired?.From?.Year;

                    bool isInYearRange = !animeYear.HasValue || Math.Abs(animeYear.Value - parsedYear) <= 1;
                    if (!isInYearRange)
                        continue;
                }

                foreach (var titleObj in anime.Titles)
                {
                    string rawTitle = titleObj.Title;
                    if (string.IsNullOrWhiteSpace(rawTitle))
                        continue;
                    rawTitle = rawTitle.ToLowerInvariant();

                    string cleanTitle;
                    if (rawTitle.Contains('('))
                    {
                        ExtractTitleAndYear(rawTitle, out cleanTitle, out _);
                        if (string.IsNullOrWhiteSpace(cleanTitle))
                            continue;
                    }
                    else
                    {
                        cleanTitle = rawTitle;
                    }

                    // Case 1: direct similarity check
                    int similarity = FuzzierSharp.Fuzz.Ratio(NormalizeRegex.Replace(cleanTitle, string.Empty), normalizedSearch);
                    if (similarity == 100)
                        return anime.MalId;

                    if (similarity >= 95 && similarity > bestSimilarity)
                    {
                        bestSimilarity = similarity;
                        bestMalId = anime.MalId;
                    }

                    // Case 2: if title contains ':', compare first part only
                    int colonIndex = rawTitle.IndexOf(':');
                    if (colonIndex <= 0) continue;

                    string firstPart = rawTitle[..colonIndex].Trim();
                    if (string.IsNullOrWhiteSpace(firstPart)) continue;
                    string cleanFirstPart;
                    if (firstPart.Contains('('))
                    {
                        ExtractTitleAndYear(firstPart, out cleanFirstPart, out _);
                        if (string.IsNullOrWhiteSpace(cleanFirstPart))
                            continue;
                    }
                    else
                    {
                        cleanFirstPart = firstPart;
                    }

                    int partSimilarity = FuzzierSharp.Fuzz.Ratio(NormalizeRegex.Replace(cleanFirstPart, string.Empty), normalizedSearch);
                    if (partSimilarity >= 95 && partSimilarity > bestBackupSimilarity)
                    {
                        bestBackupMalId = anime.MalId;
                        bestBackupSimilarity = partSimilarity;
                        continue;
                    }
                }
            }

            if (bestMalId.HasValue)
                return bestMalId;

            if (bestBackupMalId.HasValue && !ignoreBestAttempt)
                return bestBackupMalId;

            return ignoreBestAttempt
                ? null
                : await GetBestAttemptId(normalizedSearch, isMovie, hasParsedYear, parsedYear, cancellationToken).ConfigureAwait(false);
        }

        public async Task<AnimeFullCacheDto> GetCurrentAnimeSeasonAsync(ILogger _log, long malId, int seasonNumber, CancellationToken cancellationToken)
        {
            async Task<List<long>> GetRelatedAnimeIdsAsync(long id, string relationType)
            {
                var animeWithRelations = await JikanAPI.GetAnimeFullAsync(id, cancellationToken, true).ConfigureAwait(false);

                var relations = animeWithRelations?.Relations;
                if (relations == null || relations.Count == 0)
                    return new List<long>(0);

                var result = new List<long>();

                foreach (var r in relations)
                {
                    if (r?.Relation == null ||
                        !r.Relation.Equals(relationType, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var entries = r.Entry;
                    if (entries == null)
                        continue;

                    foreach (var e in entries)
                    {
                        if (e > 0)
                            result.Add(e);
                    }
                }

                return result;
            }

            static int GetTypePriority(string type) => type?.ToUpperInvariant() switch
            {
                "TV" => 0,
                "ONA" => 1,
                "TV SPECIAL" => 2,
                "MOVIE" => 3,
                "OVA" => 4,
                "SPECIAL" => 5,
                _ => int.MaxValue
            };

            async Task<AnimeFullCacheDto> GetFirstSequelAsync(long id)
            {
                var sequelIds = await GetRelatedAnimeIdsAsync(id, "Sequel").ConfigureAwait(false);

                AnimeFullCacheDto best = null;
                int bestPriority = int.MaxValue;

                foreach (var sequelId in sequelIds)
                {
                    var anime = await JikanAPI
                        .GetAnimeFullAsync(sequelId, cancellationToken)
                        .ConfigureAwait(false);

                    if (anime?.Type == null)
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

            static bool IsPartTwoOrLater(string title)
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

            static bool IsSkippable(AnimeFullCacheDto anime)
            {
                var isPart = anime.Titles?.Any(t => t.Title != null && IsPartTwoOrLater(t.Title)) == true;

                var isSpecial = anime.Titles?.Any(t =>
                    t.Title != null &&
                    (t.Title.Contains("OVA", StringComparison.OrdinalIgnoreCase) ||
                     (t.Title.Contains("Special", StringComparison.OrdinalIgnoreCase) &&
                      !t.Title.Contains("TV Special", StringComparison.OrdinalIgnoreCase)))) == true;

                var isWrongType =
                    !string.Equals(anime.Type, "TV", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(anime.Type, "ONA", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(anime.Type, "TV Special", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(anime.Type, "MOVIE", StringComparison.OrdinalIgnoreCase);

                var isSingleEpisode = anime.Episodes.GetValueOrDefault() == 1 && !string.Equals(anime.Type, "MOVIE", StringComparison.OrdinalIgnoreCase);

                return isPart || isSpecial || isWrongType || isSingleEpisode;
            }

            var anime = await JikanAPI.GetAnimeFullAsync(malId, cancellationToken).ConfigureAwait(false);
            if (anime?.MalId == null)
                return null;

            if (anime.Episodes == 1)
            {
                var next = await GetFirstSequelAsync(anime.MalId.Value).ConfigureAwait(false);
                if (next != null)
                    anime = next;
            }

            int currentSeason = 1;

            while (currentSeason < seasonNumber)
            {
                var next = await GetFirstSequelAsync(anime.MalId!.Value).ConfigureAwait(false);
                if (next == null)
                    break;

                anime = next;

                if (IsSkippable(anime)) continue;

                currentSeason++;
            }

            return anime;
        }
    }
}
