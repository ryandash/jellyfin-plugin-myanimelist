using JikanDotNet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class AnimeFullCacheDto
    {
        public long? MalId { get; set; }
        public string Url { get; set; }
        public List<TitleEntryDto> Titles { get; set; }
        public ImagesSetDto Images { get; set; }
        public TimePeriodDto Aired { get; set; }
        public AnimeBroadcastDto Broadcast { get; set; }
        public long? Duration { get; set; }
        public bool NSFW { get; set; }
        public float? Score { get; set; }
        public int? Episodes { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string[] Studios { get; set; }
        public string[] Genres { get; set; }
        public string Synopsis { get; set; }
        public List<RelatedEntryDto> Relations { get; set; }

        private static string Normalize(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static readonly HashSet<string> NSFWGenres = new(StringComparer.OrdinalIgnoreCase)
        {
            "Erotica",
            "Ecchi",
            "Hentai"
        };

        public static AnimeFullCacheDto From(AnimeFull source)
        {
            if (source is null || !source.MalId.HasValue)
                return null;

            return MapBase(
                source.MalId,
                source.Url,
                source.Titles,
                source.Images,
                source.Aired,
                source.Broadcast,
                source.Duration,
                source.Score,
                source.Episodes,
                source.Type,
                source.Status,
                source.Studios,
                source.Genres,
                source.Synopsis,
                RelatedEntryDto.FilterRelations(source.Relations)
            );
        }

        public static AnimeFullCacheDto From(Anime source)
        {
            if (source is null || !source.MalId.HasValue)
                return null;

            return MapBase(
                source.MalId,
                source.Url,
                source.Titles,
                source.Images,
                source.Aired,
                source.Broadcast,
                source.Duration,
                source.Score,
                source.Episodes,
                source.Type,
                source.Status,
                source.Studios,
                source.Genres,
                source.Synopsis,
                null
            );
        }

        private static AnimeFullCacheDto MapBase(
            long? malId,
            string url,
            ICollection<TitleEntry> titles,
            ImagesSet images,
            TimePeriod aired,
            AnimeBroadcast broadcast,
            string duration,
            double? score,
            int? episodes,
            string type,
            string status,
            ICollection<MalUrl> studios,
            ICollection<MalUrl> genres,
            string synopsis,
            List<RelatedEntryDto> relations)
        {
            var genreNames = genres?.Select(g => Normalize(g?.Name)).ToArray() ?? Array.Empty<string>();

            var studioNames = studios?.Select(s => Normalize(s?.Name)).ToArray() ?? Array.Empty<string>();

            return new AnimeFullCacheDto
            {
                MalId = malId,
                Url = Normalize(url),
                Titles = titles?.Select(TitleEntryDto.From).ToList(),
                Images = ImagesSetDto.From(images),
                Aired = TimePeriodDto.Convert(aired),
                Broadcast = AnimeBroadcastDto.From(broadcast),
                Duration = GetTicks(Normalize(duration)),
                Score = (float?)score,
                Episodes = episodes,
                Type = Normalize(type),
                Status = Normalize(status),
                Studios = studioNames,
                Genres = genreNames,
                NSFW = genreNames.Any(n => NSFWGenres.Contains(n)),
                Synopsis = Normalize(synopsis),
                Relations = relations
            };
        }

        private static long? GetTicks(string duration)
        {
            if (string.IsNullOrWhiteSpace(duration))
                return null;

            var hours = 0;
            var minutes = 0;
            var matched = false;

            var parts = duration.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!int.TryParse(parts[i], out var value))
                    continue;

                var unit = parts[i + 1];

                if (unit.StartsWith("hr", StringComparison.OrdinalIgnoreCase))
                {
                    hours += value;
                    matched = true;
                }
                else if (unit.StartsWith("min", StringComparison.OrdinalIgnoreCase))
                {
                    minutes += value;
                    matched = true;
                }
            }

            if (!matched)
                return null;

            return TimeSpan.FromMinutes(hours * 60 + minutes).Ticks;
        }
    }
}
