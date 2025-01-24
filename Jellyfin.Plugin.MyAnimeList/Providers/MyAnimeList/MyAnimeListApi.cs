using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public static class MyAnimeListApi
    {
        private static readonly HttpClient client = new HttpClient();

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
            public string name { get; set; }
            public Payload payload { get; set; }
        }

        public class Payload
        {
            public string media_type { get; set; }
        }

        private static string MyAnimeListSearchApi = "https://myanimelist.net/search/prefix.json?type=anime&keyword=";

        public static async Task<long?> GetFirstAnimeID(string searchTerm, bool isMovie, bool ignoreBestAttempt)
        {
            string url = MyAnimeListSearchApi + Uri.EscapeDataString(searchTerm);
            HttpResponseMessage response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                string jsonResponse = await response.Content.ReadAsStringAsync();

                var searchResult = JsonSerializer.Deserialize<Root>(jsonResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var filteredItems = searchResult.categories
                    .SelectMany(category => category.items)
                    .Where(item => isMovie
                        ? item.payload.media_type == "Movie"
                        : item.payload.media_type != "Movie");

                var foundItem = filteredItems.FirstOrDefault(item => item.name.Equals(searchTerm, StringComparison.OrdinalIgnoreCase));

                if (foundItem != null)
                {
                    return foundItem.id;
                }
                else if (!ignoreBestAttempt)
                {
                    return filteredItems.FirstOrDefault()?.id ?? null;
                }
            }

            return null;
        }
    }
}
