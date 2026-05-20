using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AnitomySharp;
using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
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
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly PluginConfiguration _config;

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
            _config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        }

        private static readonly Regex MalIdRegex = new Regex(@"\[mal-(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static long? ExtractMalIdFromSearchName(string searchName)
        {
            var match = MalIdRegex.Match(searchName);
            if (!match.Success)
                return null;

            if (long.TryParse(match.Groups[1].Value, out var malId))
                return malId;

            return null;
        }

        public async Task<AnimeFullCacheDto> GetAnimeAsync(ILogger _log, ItemLookupInfo info, CancellationToken cancellationToken, bool SearchResult)
        {

            bool enableDebug = _config.EnableDebug;

            string malId = info switch
            {
                EpisodeInfo episodeInfo => episodeInfo.SeasonProviderIds.GetOrDefault(ProviderNames.MyAnimeList),
                _ => info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList)
            };

            if (!string.IsNullOrEmpty(malId))
            {
                if (!_config.IgnoreMetadata || (info is EpisodeInfo && !_config.IgnoreEpisodeMetadata) || SearchResult)
                {
                    if (enableDebug) _log.LogInformation("Returned malID: {malID} for type {type}", malId, info.GetType().ToString());
                    return (await JikanAPI.GetAnimeFullAsync(long.Parse(malId), cancellationToken).ConfigureAwait(false));
                }
                else
                {
                    if (enableDebug) _log.LogInformation("Ignored malID: {malID}", malId);
                }
            }

            if (enableDebug)
            {
                _log.LogInformation("Original path: {path}", info.Path);
                _log.LogInformation("Original name: {name}", info.Name);
            }

            string searchName = GetSearchName(info, _log, enableDebug);
            if (enableDebug) _log.LogInformation("Search name: {name}", searchName);

            long malid;
            int similarityConfidence;
            long? extractedMalId = ExtractMalIdFromSearchName(searchName);
            if (extractedMalId.HasValue)
            {
                if (enableDebug) _log.LogInformation("Extracted MAL ID from name: {malId}", extractedMalId.Value);
                malid = extractedMalId.Value;
                similarityConfidence = 100;
            }
            else
            {
                (long? malIdFromName, int similarity) = await GetBestAnimeID(_log, searchName, info is MovieInfo, _config.EnableBestAttempt, cancellationToken).ConfigureAwait(false);
                if (!malIdFromName.HasValue)
                {
                    if (enableDebug) _log.LogError("Could not find MalID for: {searchName}", searchName);
                    return null;
                }
                malid = malIdFromName.Value;
                similarityConfidence = similarity;
            }

            if (enableDebug) _log.LogInformation($"Found MalID: {malid}", malid);
            return info switch
            {
                MovieInfo => (await JikanAPI.GetAnimeFullAsync(malid, cancellationToken).ConfigureAwait(false)),
                EpisodeInfo => await GetCurrentAnimeSeasonAsync(_log, malid, similarityConfidence, info.ParentIndexNumber ?? 1, cancellationToken).ConfigureAwait(false),
                _ => await GetCurrentAnimeSeasonAsync(_log, malid, similarityConfidence, info.IndexNumber ?? 1, cancellationToken).ConfigureAwait(false)
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
            ExtractTitleAndYear(searchTerm, out string searchTitle, out string year);
            bool hasParsedYear = int.TryParse(year, out int parsedYear);

            var searchResults = await JikanAPI.SearchAnimeAsync(searchTitle, cancellationToken).ConfigureAwait(false);
            string normalizedSearch = NormalizeRegex.Replace(searchTitle, string.Empty).ToLowerInvariant();

            Func<string, bool> mediaTypeCondition = isMovie
                ? mediaType => string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase)
                : mediaType => !string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase);

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

                    int similarity = FuzzierSharp.Fuzz.Ratio(NormalizeRegex.Replace(cleanTitle, string.Empty), normalizedSearch);
                    if (similarity == 100)
                        return (anime.MalId, similarity);

                    if (similarity >= 95 && similarity > bestSimilarity)
                    {
                        bestSimilarity = similarity;
                        bestMalId = anime.MalId;
                    }
                }
            }

            if (bestMalId.HasValue)
                return (bestMalId, bestSimilarity);

            return enableBestAttempt
                ? await GetBestAttemptId(normalizedSearch, isMovie, hasParsedYear, parsedYear, _httpClientFactory, cancellationToken).ConfigureAwait(false)
                : (null, 0);
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
