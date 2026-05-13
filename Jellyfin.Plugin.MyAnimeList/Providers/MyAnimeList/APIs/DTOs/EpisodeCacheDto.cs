using System;
using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class EpisodeCacheDto
    {
        public long EpisodeNumber { get; set; }

        public string Title { get; set; }

        public string TitleJapanese { get; set; }

        public string TitleRomanji { get; set; }

        public DateTime? Aired { get; set; }

        public double? Score { get; set; }

        public string Url { get; set; }

        public int? Duration { get; set; }

        public string Synopsis { get; set; }

        public bool HasFullDetails => !string.IsNullOrWhiteSpace(Synopsis)
            && Duration.HasValue;

        public static EpisodeCacheDto From(AnimeEpisode source)
        {
            if (source == null) return null;

            return new EpisodeCacheDto
            {
                EpisodeNumber = source.MalId,
                Url = source.Url,
                Title = source.Title,
                TitleJapanese = source.TitleJapanese,
                TitleRomanji = source.TitleRomanji,
                Aired = source.Aired,
                Score = source.Score,
            };
        }

        public static void MergeEpisodeDetails(EpisodeCacheDto existing, EpisodeCacheDto detailed)
        {
            existing.Duration ??= detailed.Duration;
            existing.Synopsis ??= string.IsNullOrWhiteSpace(detailed.Synopsis) ? null : detailed.Synopsis;
        }
    }
}
