using JikanDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs
{
    public static class MyAnimeListApi
    {
        private static readonly HttpClient _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        private static readonly Jikan _jikan = JikanSingleton.Instance;

        private const string MyAnimeListSearchApi = "https://myanimelist.net/search/prefix.json?type=anime&keyword=";

        public class Root
        {
            public List<Category> categories { get; set; }
        }

        public class Category
        {
            public List<Item> items { get; set; }
        }

        public class Item
        {
            public int id { get; set; }
            public Payload payload { get; set; }
        }

        public class Payload
        {
            public string media_type { get; set; }
        }

        public static async Task<long?> GetBestAnimeID(string searchTerm, bool isMovie, bool ignoreBestAttempt)
        {
            string url = $"{MyAnimeListSearchApi}{Uri.EscapeDataString(searchTerm)}";
            var response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();
            var searchResult = JsonSerializer.Deserialize<Root>(jsonResponse, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            Func<string, bool> mediaTypeCondition = isMovie
                ? mediaType => string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase)
                : mediaType => !string.Equals(mediaType, "Movie", StringComparison.OrdinalIgnoreCase);

            var filteredItems = searchResult.categories
                .SelectMany(category => category.items)
                .Where(item => mediaTypeCondition(item.payload.media_type))
                .ToList();

            long? backupID = null;
            foreach (var item in filteredItems)
            {
                var animeDetails = await _jikan.GetAnimeAsync(item.id);
                foreach (var title in animeDetails.Data.Titles)
                {
                    string originalTitle = title.Title;
                    string normalizedTitle = originalTitle.Replace(":", string.Empty);

                    if (string.Equals(originalTitle, searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(normalizedTitle, searchTerm, StringComparison.OrdinalIgnoreCase))
                    {
                        return item.id;
                    }

                    if (backupID == null && originalTitle.Contains(':'))
                    {
                        int colonIndex = originalTitle.IndexOf(':');
                        string firstPart = originalTitle[..colonIndex].Trim();
                        if (string.Equals(firstPart, searchTerm, StringComparison.OrdinalIgnoreCase))
                        {
                            backupID = item.id;
                        }
                    }
                }
            }

            return backupID ?? (ignoreBestAttempt ? null : filteredItems.FirstOrDefault()?.id);
        }
    }
}
