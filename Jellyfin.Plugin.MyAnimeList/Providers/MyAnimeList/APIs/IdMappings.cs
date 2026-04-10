using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs
{
    public class IdMappings
    {
        private readonly string _baseUrl = "https://github-checker-worker.ryandash0.workers.dev/";
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public IdMappings()
        {
            _httpClient = Plugin.Instance.GetHttpClient();
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

        private async Task<List<MappingEntry>> GetMappingsAsync(ILogger log, string tvdbId, CancellationToken token)
        {
            var url = $"{_baseUrl}thetvdb-episodes?id={tvdbId}";

            try
            {
                using var stream = await _httpClient.GetStreamAsync(url, token)
                    .ConfigureAwait(false);

                var result = await JsonSerializer.DeserializeAsync<List<MappingEntry>>(
                    stream,
                    _options,
                    token
                ).ConfigureAwait(false);

                return result ?? new List<MappingEntry>();
            }
            catch (Exception ex)
            {
                log.LogInformation($"Error fetching mappings from {url}: {ex.Message}");
                return new List<MappingEntry>();
            }
        }

        public class AnimeEpisodeMapping
        {
            public long? MalId { get; set; }
            public int? Episode { get; set; }
        }

        private static int? ExtractMalEpisode(string malUrl)
        {
            if (string.IsNullOrEmpty(malUrl))
                return null;

            var idx = malUrl.IndexOf("/episode/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            idx += 9; // length of "/episode/"

            int end = malUrl.IndexOf('/', idx);
            if (end < 0) end = malUrl.Length;

            return int.TryParse(malUrl.AsSpan(idx, end - idx), out var ep)
                ? ep
                : null;
        }

        public async Task<AnimeEpisodeMapping> GetAnimeEpisodeMappingAsync(ILogger _log, string tvdbId, CancellationToken token)
        {
            var mappings = await GetMappingsAsync(_log, tvdbId, token).ConfigureAwait(false);
            var entry = mappings.FirstOrDefault();
            if (entry == null)
            {
                _log.LogInformation($"No mapping found for TVDB ID: {tvdbId}");
                return null;
            }

            return new AnimeEpisodeMapping
            {
                MalId = entry.MalId,
                Episode = ExtractMalEpisode(entry.MalUrl)
            };
        }
    }
}
