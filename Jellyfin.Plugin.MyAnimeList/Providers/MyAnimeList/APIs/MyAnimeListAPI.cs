using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs
{
    public static class MyAnimeListApi
    {
        private const string MyAnimeListSearchApi = "https://myanimelist.net/search/prefix.json?type=anime&keyword=";

        public class Root
        {
            public List<Category> categories { get; set; }
        }

        public class Category
        {
            public string type { get; set; }
            public List<Item> items { get; set; }
        }

        public class Item
        {
            public int id { get; set; }
            public string name { get; set; }
            public Payload payload { get; set; }
        }

        public class Payload
        {
            public string media_type { get; set; }
        }

        private static readonly Regex NormalizeRegex = new Regex("[:.!]", RegexOptions.Compiled);
        public static async Task<(long?, int)> GetBestAttemptId(string searchTerm, bool isMovie, bool hasParsedYear, int parsedYear, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken)
        {
            string url = $"{MyAnimeListSearchApi}{Uri.EscapeDataString(searchTerm)}";

            var client = httpClientFactory.CreateClient(ProviderNames.MyAnimeList);

            var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (null, 0);

            string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var searchResult = JsonSerializer.Deserialize<Root>(jsonResponse) ?? new Root();

            Func<string, bool> mediaTypeCondition = isMovie
                ? mediaType => string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase)
                : mediaType => !string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase);

            int highestSimilarity = 0;
            var bestItem = (Item)null;
            var animeCategory = searchResult.categories?.FirstOrDefault(c => c.type is "anime");
            if (animeCategory is null)
                return (null, 0);

            string normalizedSearch = NormalizeRegex.Replace(searchTerm, string.Empty).ToLowerInvariant();

            foreach (var item in animeCategory.items)
            {
                if (!mediaTypeCondition(item.payload.media_type)) continue;
                AnimeFullCacheDto anime = await JikanAPI.GetAnimeFullAsync(item.id, cancellationToken).ConfigureAwait(false);
                if (hasParsedYear)
                {
                    int? animeYear = anime.Aired?.From?.Year;

                    bool isInYearRange = !animeYear.HasValue || Math.Abs(animeYear.Value - parsedYear) <= 1;
                    if (!isInYearRange)
                        continue;
                }

                foreach (var titleObj in anime.Titles)
                {
                    var similarity = FuzzierSharp.Fuzz.Ratio(titleObj.Title.Replace(searchTerm, string.Empty).ToLowerInvariant(), normalizedSearch);
                    if (similarity == 100)
                        return (item.id, similarity);

                    if (similarity > highestSimilarity)
                    {
                        highestSimilarity = similarity;
                        bestItem = item;
                    }
                }
            }

            return highestSimilarity > 95 ? (bestItem?.id, highestSimilarity) : (searchResult.categories.FirstOrDefault()?.items.FirstOrDefault()?.id, 50);
        }
    }
}
