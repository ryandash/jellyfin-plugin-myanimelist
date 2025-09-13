using JikanDotNet;
using System;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class EpisodeCacheDto
    {
        public long? MalId { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public string TitleJapanese { get; set; }
        public string TitleRomanji { get; set; }
        public int? Duration { get; set; }
        public DateTime? Aired { get; set; }
        public string Synopsis { get; set; }

        public static EpisodeCacheDto From(AnimeEpisode source)
        {
            if (source == null) return null;

            return new EpisodeCacheDto
            {
                MalId = source.MalId,
                Url = source.Url,
                Title = source.Title,
                TitleJapanese = source.TitleJapanese,
                TitleRomanji = source.TitleRomanji,
                Duration = source.Duration,
                Aired = source.Aired,
                Synopsis = string.IsNullOrWhiteSpace(source.Synopsis) ? null : source.Synopsis
            };
        }
    }
}
