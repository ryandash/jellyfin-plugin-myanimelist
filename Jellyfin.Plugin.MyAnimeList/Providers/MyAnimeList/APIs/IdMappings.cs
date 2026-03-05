using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs
{
    public class IdMappings
    {
        private readonly string baseUrl = "https://ryandash.github.io/TVDB-IDs-To-MyAnimeList-IDs/api/thetvdb-series/{0}.json";
        //private readonly string movieUrl = "https://ryandash.github.io/TVDB-IDs-To-MyAnimeList-IDs/api/thetvdb-movie/{0}.json";
        private readonly HttpClient httpClient;

        public IdMappings(HttpClient client = null)
        {
            httpClient = client ?? new HttpClient();
        }

        private class MappingEntry
        {
            [JsonPropertyName("season")]
            public int? Season { get; set; }

            [JsonPropertyName("episode")]
            public int? Episode { get; set; }

            [JsonPropertyName("thetvdb url")]
            public string TvdbUrl { get; set; }

            [JsonPropertyName("myanimelist url")]
            public string MalUrl { get; set; }

            [JsonPropertyName("myanimelist")]
            public long? MalId { get; set; }

            [JsonPropertyName("thetvdb")]
            public string Tvdb { get; set; }
        }

        private async Task<List<MappingEntry>> GetMappingsAsync(ILogger _log, string tvdbId)
        {
            string url = string.Format(baseUrl, tvdbId);

            try
            {
                var stream = await httpClient.GetStreamAsync(url);

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var result = await JsonSerializer.DeserializeAsync<List<MappingEntry>>(stream, options);

                return result ?? new List<MappingEntry>();
            }
            catch (Exception ex)
            {
                _log.LogInformation($"Error fetching mappings: {ex.Message}");
                return new List<MappingEntry>();
            }
        }

        public async Task<AnimeEpisodeMapping> GetAnimeEpisodeMappingAsync(ILogger _log, string tvdbId)
        {
            var mappings = await GetMappingsAsync(_log, tvdbId);
            var entry = mappings.FirstOrDefault();
            if (entry == null)
            {
                _log.LogInformation($"No mapping found for TVDB ID: {tvdbId}");
                return null;
            }
            return new AnimeEpisodeMapping
            {
                MalId = entry.MalId,
                MalUrl = entry.MalUrl,
                Episode = entry.Episode,
                Season = entry.Season
            };
        }
    }

    public class AnimeEpisodeMapping
    {
        public long? MalId { get; set; }
        public string MalUrl { get; set; }
        public int? Episode { get; set; }
        public int? Season { get; set; }
    }
}
