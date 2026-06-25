using AnitomySharp;
using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static AnitomySharp.AnitomySharp;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class MyAnimeListSearchHelper
    {
        private readonly string[] _libraryRoots;
        private readonly IHttpClientFactory _httpClientFactory;
        private static PluginConfiguration _config => Plugin.Instance?.Configuration ?? new PluginConfiguration
        {
            EnableDebug = true,
            EnableBestAttempt = true
        };

        public MyAnimeListSearchHelper(ILibraryManager libraryManager, IHttpClientFactory httpClientFactory)
        {
            _libraryRoots = libraryManager
                .GetVirtualFolders()
                .SelectMany(v => v.Locations)
                .Where(l => !string.IsNullOrEmpty(l))
                .Select(l => l.TrimEnd(Path.DirectorySeparatorChar))
                .OrderByDescending(l => l.Length)
                .ToArray();

            _httpClientFactory = httpClientFactory;
        }

        private static readonly Regex MalIdRegex = new Regex(@"\[mal-(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public async Task<AnimeFullCacheDto> GetAnimeAsync(ILogger _log, ItemLookupInfo info, CancellationToken cancellationToken, bool SearchResult)
        {

            bool debug = _config.EnableDebug;
            bool forceNew = _config.ForceNewMetadata;

            long? malId = TryGetDirectMalId(info);

            if (malId.HasValue && (!forceNew || SearchResult))
            {
                if (debug) _log.LogInformation("Returned malID: {malID} for type {type}", malId, info.GetType().ToString());
                return (await JikanAPI.GetAnimeFullAsync(malId.Value, cancellationToken).ConfigureAwait(false));
            }

            if (debug)
            {
                _log.LogInformation("Original path: {path}", info.Path);
                _log.LogInformation("Original name: {name}", info.Name);
            }

            string searchTerm = GetSearchName(info, _log, debug);

            int? confidence = null;

            malId ??= TryGetSeriesOrEpisodeMalId(info, out confidence);

            if (!malId.HasValue && !string.IsNullOrWhiteSpace(searchTerm))
            {
                malId = ExtractMalIdFromSearchName(searchTerm);
                if (malId.HasValue)
                {
                    confidence = 100;
                    if (debug) _log.LogInformation("Extracted MAL ID {MalId} from name", malId);
                }
            }

            string searchName = (!string.IsNullOrWhiteSpace(info.Name) && !forceNew) ? info.Name : (!malId.HasValue ? searchTerm : null);

            if (!malId.HasValue && !string.IsNullOrWhiteSpace(searchName))
            {
                if (debug) _log.LogInformation("Search using name: {name}", searchName);
                (malId, int similarity) = await GetBestAnimeID(_log, searchName, info is MovieInfo, _config.EnableBestAttempt, cancellationToken).ConfigureAwait(false);
                if (malId.HasValue)
                {
                    confidence = similarity;
                }
                else if (debug) _log.LogError("Could not find MalID using Name: {name}", searchName);
            }

            if (!malId.HasValue)
            {
                if (debug) _log.LogError("Failed to Find MAL ID");
                return null;
            }

            if (debug) _log.LogInformation("Found MalID: {MalId}", malId);

            int seasonNumber = info switch
            {
                EpisodeInfo e => e.ParentIndexNumber ?? 1,
                _ => info.IndexNumber ?? 1
            };

            if (info is MovieInfo)
            {
                return await JikanAPI.GetAnimeFullAsync(malId.Value, cancellationToken).ConfigureAwait(false);
            }

            int conf = confidence ?? 100;

            return await GetCurrentAnimeSeasonAsync(_log, malId.Value, conf, seasonNumber, cancellationToken).ConfigureAwait(false);
        }

        private static long? TryGetDirectMalId(ItemLookupInfo info)
        {
            string id = info switch
            {
                EpisodeInfo e => e.SeasonProviderIds.GetOrDefault(ProviderNames.MyAnimeList),
                _ => info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList)
            };

            return long.TryParse(id, out var value) ? value : null;
        }

        private static long? TryGetSeriesOrEpisodeMalId(ItemLookupInfo info, out int? confidence)
        {
            confidence = null;

            if (info is SeasonInfo season &&
                long.TryParse(season.SeriesProviderIds.GetOrDefault(ProviderNames.MyAnimeList), out var seasonId))
            {
                confidence = 100;
                return seasonId;
            }

            if (info is EpisodeInfo episode &&
                long.TryParse(episode.SeriesProviderIds.GetOrDefault(ProviderNames.MyAnimeList), out var epId))
            {
                confidence = 100;
                return epId;
            }

            return null;
        }

        private static long? ExtractMalIdFromSearchName(string searchName)
        {
            var match = MalIdRegex.Match(searchName);
            if (!match.Success)
                return null;

            if (long.TryParse(match.Groups[1].Value, out var malId))
                return malId;

            return null;
        }

        private static readonly char[] separator = ['-', '_'];
        private string GetSearchName(ItemLookupInfo info, ILogger _log, bool enableDebug)
        {
            if (string.IsNullOrEmpty(info.Path))
                return info.Name;
            string relativePath = StripLibraryPath(info.Path, _log, enableDebug);
            var splitPath = relativePath.Split(Path.DirectorySeparatorChar);
            if (enableDebug) _log.LogInformation($"Remaining strings: \"{string.Join("\", \"", splitPath)}\"");
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
                    string title = Path.GetFileNameWithoutExtension(info.Path)
                        .Split(separator, StringSplitOptions.RemoveEmptyEntries)
                        .LastOrDefault()?
                        .Trim();

                    return (episode.ParentIndexNumber == 0
                        && !string.IsNullOrWhiteSpace(title)
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

                if (title is not null && year is not null)
                    return;
            }
        }

        private async Task<(long?, int)> GetBestAnimeID(ILogger _log, string searchTerm, bool isMovie, bool enableBestAttempt, CancellationToken cancellationToken)
        {
            bool enableDebug = _config.EnableDebug;
            bool enableNSFW = _config.EnableNSFW;

            ExtractTitleAndYear(searchTerm, out string searchTitle, out string year);

            bool hasParsedYear = int.TryParse(year, out int parsedYear);

            var searchResults = (await JikanAPI.SearchAnimeAsync(searchTitle, enableNSFW, isMovie, cancellationToken).ConfigureAwait(false)).ToList();

            if (enableDebug) _log.LogInformation($"Found {searchResults.Count} search results");

            string Normalize(string input) => NormalizeRegex.Replace(input, string.Empty).Trim().ToLowerInvariant();

            string normalizedSearch = Normalize(searchTitle);

            long? bestMalId = null;
            int bestSimilarity = -1;

            var orderedSearchResults = searchResults.OrderBy(m => m.MalId).ToList();

            foreach (var anime in orderedSearchResults)
            {
                int? animeYear = anime.Aired?.From?.Year;

                if (hasParsedYear && animeYear.HasValue &&
                    Math.Abs(animeYear.Value - parsedYear) > 1)
                    continue;

                var malId = anime.MalId;

                foreach (var titleObj in anime.Titles)
                {
                    var rawTitle = titleObj.Title;
                    if (string.IsNullOrWhiteSpace(rawTitle))
                        continue;

                    string cleanTitle;
                    if (rawTitle.Contains('(')) {
                        ExtractTitleAndYear(rawTitle, out cleanTitle, out _);

                        if (string.IsNullOrWhiteSpace(cleanTitle)) continue;
                    }
                    else
                    {
                        cleanTitle = rawTitle;
                    }

                    int similarity = FuzzierSharp.Fuzz.Ratio(Normalize(cleanTitle), normalizedSearch);

                    if (similarity == 100)
                        return (malId, similarity);

                    if (similarity >= 90 && similarity > bestSimilarity)
                    {
                        bestSimilarity = similarity;
                        bestMalId = malId;
                    }
                }
            }

            if (bestMalId.HasValue)
                return (bestMalId, bestSimilarity);

            if (!enableBestAttempt) return (null, 0);

            if (!isMovie) {
                var firstFallback = orderedSearchResults.FirstOrDefault(a => a.Titles.Any(t => t.Title.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)));

                if (firstFallback != null) return (firstFallback.MalId, 50);
            }

            return searchResults.Count > 0 ? (searchResults[0].MalId, 50): (null, 0);
        }

        public async Task<AnimeFullCacheDto> GetCurrentAnimeSeasonAsync(ILogger _log, long malId, int similarityConfidence, int seasonNumber, CancellationToken cancellationToken)
        {
            async Task<List<long>> GetRelatedAnimeIdsAsync(long id, string relationType)
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
                var isPart = anime.Titles?.Any(t => t.Title is not null && IsPartTwoOrLater(t.Title)) is true;

                var isSpecial = anime.Titles?.Any(t =>
                    t.Title is not null &&
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

            if (similarityConfidence < 90)
            {
                var visited = new HashSet<long>();

                while (visited.Add(malId))
                {
                    var prequelIds = await GetRelatedAnimeIdsAsync(malId, "Prequel").ConfigureAwait(false);
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
                var next = await GetFirstSequelAsync(anime.MalId.Value).ConfigureAwait(false);
                if (next is not null)
                    anime = next;
            }

            int currentSeason = 1;

            while (currentSeason < seasonNumber)
            {
                var next = await GetFirstSequelAsync(anime.MalId!.Value).ConfigureAwait(false);
                if (next is null)
                    break;

                anime = next;

                if (IsSkippable(anime)) continue;

                currentSeason++;
            }

            return anime;
        }

        public async Task<(int episodeNumber, AnimeFullCacheDto anime)> GetSeasonEpisodeNumberAsync(
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
