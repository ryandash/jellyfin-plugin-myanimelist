using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.Helpers
{
    internal class SearchService
    {
        private static readonly Regex NormalizeRegex = new Regex("[:.!]", RegexOptions.Compiled);

        public static async Task<(long?, int)> SearchForBestAnimeID(ILogger _log, string searchTerm, bool isMovie, bool specialEpisode, PluginConfiguration config, CancellationToken cancellationToken)
        {
            bool enableDebug = config.EnableDebug;
            bool enableNSFW = config.EnableNSFW;

            NameBuilder.ExtractTitleAndYear(searchTerm, out string searchTitle, out string year, specialEpisode);

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
                    if (rawTitle.Contains('('))
                    {
                        NameBuilder.ExtractTitleAndYear(rawTitle, out cleanTitle, out _);

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

            if (!config.EnableBestAttempt) return (null, 0);

            if (!isMovie)
            {
                var firstFallback = orderedSearchResults.FirstOrDefault(a => a.Titles.Any(t => t.Title.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)));

                if (firstFallback != null) return (firstFallback.MalId, 50);
            }

            return searchResults.Count > 0 ? (searchResults[0].MalId, 50) : (null, 0);
        }
    }
}
