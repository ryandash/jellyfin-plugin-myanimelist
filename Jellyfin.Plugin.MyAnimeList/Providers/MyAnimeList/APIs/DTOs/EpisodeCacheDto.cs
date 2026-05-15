using System;
using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class EpisodeCacheDto
    {
        public int EpisodeNumber { get; set; }

        public string Title { get; set; }

        public string TitleJapanese { get; set; }

        public string TitleRomanji { get; set; }

        public DateTime? Aired { get; set; }

        public double? Score { get; set; }

        public string Url { get; set; }

        public int? Duration { get; set; }

        public string Synopsis { get; set; }

        public bool HasFullDetails => !string.IsNullOrWhiteSpace(Synopsis)
             && !string.IsNullOrWhiteSpace(Url) && Duration.HasValue;

        public static EpisodeCacheDto From(AnimeEpisode source)
        {
            if (source is null) return null;

            return new EpisodeCacheDto
            {
                EpisodeNumber = (int)source.MalId,
                Url = source.Url,
                Title = source.Title,
                TitleJapanese = source.TitleJapanese,
                TitleRomanji = source.TitleRomanji,
                Aired = source.Aired,
                Score = source.Score ?? null,
                Duration = source.Duration ?? null,
                Synopsis = source.Synopsis ?? null,
            };
        }

        public static EpisodeCacheDto MergeEpisodeDetails(EpisodeCacheDto existing, EpisodeCacheDto detailed)
        {
            if (existing is null)
                return detailed;

            if (detailed is null)
                return existing;

            if (existing.EpisodeNumber <= 0)
                existing.EpisodeNumber = detailed.EpisodeNumber;

            if (string.IsNullOrWhiteSpace(existing.Title))
                existing.Title = detailed.Title;

            if (string.IsNullOrWhiteSpace(existing.TitleJapanese))
                existing.TitleJapanese = detailed.TitleJapanese;

            if (string.IsNullOrWhiteSpace(existing.TitleRomanji))
                existing.TitleRomanji = detailed.TitleRomanji;

            existing.Aired ??= detailed.Aired;

            if (string.IsNullOrWhiteSpace(existing.Url))
                existing.Url = detailed.Url;

            existing.Duration = detailed.Duration;
            existing.Synopsis = detailed.Synopsis;
            return existing;
        }
    }
}
