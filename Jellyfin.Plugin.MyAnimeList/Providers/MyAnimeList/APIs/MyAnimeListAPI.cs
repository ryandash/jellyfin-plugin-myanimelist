using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
            public List<Item> items { get; set; }
        }

        public class Item
        {
            public int id { get; set; }
        }

        public static async Task<long?> GetBestAttemptId(string searchTerm, CancellationToken cancellationToken)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string url = $"{MyAnimeListSearchApi}{Uri.EscapeDataString(searchTerm)}";

            var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            string jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            var searchResult = JsonSerializer.Deserialize<Root>(jsonResponse) ?? new Root();

            return searchResult.categories.FirstOrDefault()?.items.FirstOrDefault()?.id;
        }
    }
}
