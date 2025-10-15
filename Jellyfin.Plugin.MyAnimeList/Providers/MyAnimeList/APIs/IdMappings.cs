using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs
{
    public class IdMappings
    {
        private readonly string baseUrl = "https://ryandash.github.io/MyAnimeList-IDs-To-TVDB-IDs/api/thetvdb/{0}.json";
        private readonly HttpClient httpClient;

        public IdMappings(HttpClient? client = null)
        {
            httpClient = client ?? new HttpClient();
        }

        // Model for JSON mapping (matches API JSON)
        private class MappingEntry
        {
            public string? season { get; set; }
            public string? episode { get; set; }
            public string? tvdb { get; set; }

            [JsonPropertyName("tvdb url")]
            public string? TvdbUrl { get; set; }

            [JsonPropertyName("myanimelist url")]
            public string? MyAnimeListUrl { get; set; }
        }

        private async Task<List<MappingEntry>> GetMappingsAsync(string tvdbId)
        {
            string url = string.Format(baseUrl, tvdbId);

            try
            {
                var response = await httpClient.GetStringAsync(url);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = JsonSerializer.Deserialize<List<MappingEntry>>(response, options);

                return result ?? new List<MappingEntry>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching mappings: {ex.Message}");
                return new List<MappingEntry>();
            }
        }

        private static long? ExtractMalIdFromUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            // MyAnimeList URLs are like: https://myanimelist.net/anime/59130/...
            var match = Regex.Match(url, @"myanimelist\.net/anime/(\d+)");
            if (match.Success && long.TryParse(match.Groups[1].Value, out long malId))
            {
                return malId;
            }

            return null;
        }

        /// <summary>
        /// Returns a mapping that contains MAL ID, MAL URL, Season, and Episode for the given TVDB ID.
        /// </summary>
        public async Task<AnimeEpisodeMapping?> GetAnimeEpisodeMappingAsync(string tvdbId)
        {
            var entry = (await GetMappingsAsync(tvdbId)).FirstOrDefault();
            if (entry == null) return null;

            return new AnimeEpisodeMapping
            {
                MalUrl = entry.MyAnimeListUrl,
                MalId = ExtractMalIdFromUrl(entry.MyAnimeListUrl),
                Episode = int.TryParse(entry.episode, out int ep) ? ep : null,
                Season = int.TryParse(entry.season, out int s) ? s : null
            };
        }
    }

    public class AnimeEpisodeMapping
    {
        public long? MalId { get; set; }
        public string? MalUrl { get; set; }
        public int? Episode { get; set; }
        public int? Season { get; set; }
    }
}
