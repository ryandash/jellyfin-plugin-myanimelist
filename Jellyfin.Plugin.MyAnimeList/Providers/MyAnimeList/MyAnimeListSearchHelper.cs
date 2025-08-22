using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs;
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
        private readonly Jikan _jikan;

        public MyAnimeListSearchHelper(Jikan _jikan)
        {
            this._jikan = _jikan;
        }

        public async Task<JikanDotNet.Anime> GetAnimeAsync(ILogger _log, ItemLookupInfo info, CancellationToken cancellationToken)
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
                    return (await _jikan.GetAnimeAsync(long.Parse(malId), cancellationToken).ConfigureAwait(false)).Data;
                }
                else
                {
                    if (enableDebug) _log.LogInformation("Ignored malID: {malID}", malId);
                }
            }

            if (enableDebug) _log.LogInformation("Original path: {path}", info.Path);
            string searchName = GetSearchName(info);
            if (enableDebug) _log.LogInformation("Original name: {name}", searchName);
            long? malIdFromName = await GetBestAnimeID(_log, FilterName(searchName), info is MovieInfo, config.IgnoreBestAttempt, cancellationToken);
            if (!malIdFromName.HasValue)
            {
                if (enableDebug) _log.LogError("Could not find MalID for: {searchName}", searchName);
                return null;
            }

            if (enableDebug) _log.LogInformation("Found MalID: {malIdFromName}", malIdFromName.Value);
            return info switch
            {
                MovieInfo => (await _jikan.GetAnimeAsync(malIdFromName.Value, cancellationToken).ConfigureAwait(false)).Data,
                _ => await GetCurrentAnimeSeasonAsync(_log, enableDebug, malIdFromName.Value, info.IndexNumber ?? 1, cancellationToken)
            };
        }

        private string GetSearchName(ItemLookupInfo info)
        {
            string[] splitPath = info.Path.Split(Path.DirectorySeparatorChar);
            return info switch
            {
                SeasonInfo => splitPath[^1].Contains("season", StringComparison.OrdinalIgnoreCase)
                    ? splitPath[^2]
                    : splitPath[^1],
                EpisodeInfo => splitPath[^2].Contains("season", StringComparison.OrdinalIgnoreCase)
                    ? splitPath[^3]
                    : splitPath[^2],
                SeriesInfo => splitPath[^1],
                MovieInfo => splitPath.Length > 2 ? splitPath[^2] : splitPath[^1],
                _ => info.Name
            };
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
            var searchResults = await _jikan.SearchAnimeAsync(searchTerm, cancellationToken);
            string normalizedSearch = NormalizeRegex.Replace(searchTerm, string.Empty).ToLowerInvariant();

            long? bestBackupMalId = null;
            int bestBackupSimilarity = -1;

            foreach (var anime in searchResults.Data)
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

        private async Task<JikanDotNet.Anime> GetCurrentAnimeSeasonAsync(
    ILogger _log, bool enableDebug,
    long malId, int seasonNumber, CancellationToken cancellationToken)
        {
            var animeCache = new Dictionary<long, JikanDotNet.Anime>();
            var relationCache = new Dictionary<long, ICollection<RelatedEntry>>();

            async Task<JikanDotNet.Anime> GetAnimeAsync(long id)
            {
                if (!animeCache.TryGetValue(id, out var anime))
                {
                    anime = (await _jikan.GetAnimeAsync(id, cancellationToken).ConfigureAwait(false)).Data;
                    animeCache[id] = anime;
                }
                return anime;
            }

            async Task<long?> GetRelatedAnimeIdAsync(long id, string relationType)
            {
                if (!relationCache.TryGetValue(id, out var relations))
                {
                    relations = (await _jikan.GetAnimeRelationsAsync(id, cancellationToken).ConfigureAwait(false)).Data;
                    relationCache[id] = relations;
                }

                return relations
                    .FirstOrDefault(r => string.Equals(r.Relation, relationType, StringComparison.OrdinalIgnoreCase))
                    ?.Entry?.FirstOrDefault()?.MalId;
            }

            var anime = await GetAnimeAsync(malId);

            if (anime.Titles.Any(t =>
                    t.Title.Contains("2nd", StringComparison.OrdinalIgnoreCase) ||
                    t.Title.Contains("Season 2", StringComparison.OrdinalIgnoreCase)))
            {
                var prequelId = await GetRelatedAnimeIdAsync(malId, "Prequel");
                if (prequelId != null)
                {
                    malId = prequelId.Value;
                    anime = await GetAnimeAsync(malId);
                }
            }

            if (anime.Episodes == 1)
            {
                var sequelId = await GetRelatedAnimeIdAsync(malId, "Sequel");
                if (sequelId != null)
                {
                    malId = sequelId.Value;
                    anime = await GetAnimeAsync(malId);
                }
            }

            for (int currentSeason = 1; currentSeason < seasonNumber; currentSeason++)
            {
                var sequelId = await GetRelatedAnimeIdAsync(malId, "Sequel");
                if (sequelId == null) break;

                malId = sequelId.Value;
                anime = await GetAnimeAsync(malId);

                if (anime.Titles.Any(t => t.Title.Contains("part ", StringComparison.OrdinalIgnoreCase)) ||
                    anime.Episodes == 1 ||
                    !(string.Equals(anime.Type, "TV", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(anime.Type, "ONA", StringComparison.OrdinalIgnoreCase)))
                {
                    seasonNumber++;
                }
            }

            return anime;
        }
    }
}
