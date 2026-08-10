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
        public ImagesSetDto Images { get; set; }
        public List<TitleEntryDto> Titles { get; set; }
        public string Type { get; set; }
        public int? Episodes { get; set; }
        public string Status { get; set; }
        public TimePeriodDto Aired { get; set; }
        public long? Duration { get; set; }
        public string Rating { get; set; }
        public float? Score { get; set; }
        public string Synopsis { get; set; }
        public AnimeBroadcastDto Broadcast { get; set; }
        public string[] Studios { get; set; }
        public string[] Genres { get; set; }
        public List<RelatedEntryDto> Relations { get; set; }
        public bool NSFW { get; set; }

        private static string Normalize(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        private static readonly HashSet<string> NSFWGenres = new(StringComparer.OrdinalIgnoreCase)
        {
            "Erotica",
            "Ecchi",
            "Hentai"
        };

        private static string MapParentalRating(string malRating)
        {
            if (string.IsNullOrWhiteSpace(malRating))
                return null;

            return malRating switch
            {
                "G - All Ages" => "G",
                "PG - Children" => "PG",
                "PG-13 - Teens 13 or older" => "13",
                "R - 17+ (violence & profanity)" => "16+",
                "R+ - Mild Nudity" => "TV-14",
                "Rx - Hentai" => "18+",
                _ => null
            };
        }

        public static AnimeFullCacheDto From(AnimeFull source)
        {
            if (source is null || !source.MalId.HasValue)
                return null;

            return MapBase(
                source.MalId,
                source.Url,
                source.Images,
                source.Titles,
                source.Type,
                source.Episodes,
                source.Status,
                source.Airing,
                source.Aired,
                source.Duration,
                source.Rating,
                source.Score,
                source.Synopsis,
                source.Broadcast,
                source.Studios,
                source.Genres,
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
                source.Images,
                source.Titles,
                source.Type,
                source.Episodes,
                source.Status,
                source.Airing,
                source.Aired,
                source.Duration,
                source.Rating,
                source.Score,
                source.Synopsis,
                source.Broadcast,
                source.Studios,
                source.Genres,
                null
            );
        }

        private static AnimeFullCacheDto MapBase(
            long? malId,
            string url,
            ImagesSet images,
            ICollection<TitleEntry> titles,
            string type,
            int? episodes,
            string status,
            bool airing,
            TimePeriod aired,
            string duration,
            string rating,
            double? score,
            string synopsis,
            AnimeBroadcast broadcast,
            ICollection<MalUrl> studios,
            ICollection<MalUrl> genres,
            List<RelatedEntryDto> relations)
        {
            var genreNames = genres?.Select(g => Normalize(g?.Name)).ToArray() ?? Array.Empty<string>();

            var studioNames = studios?.Select(s => Normalize(s?.Name)).ToArray() ?? Array.Empty<string>();

            return new AnimeFullCacheDto()
            {
                MalId = malId,
                Url = Normalize(url),
                Images = ImagesSetDto.From(images),
                Titles = titles?.Select(TitleEntryDto.From).ToList(),
                Type = Normalize(type),
                Episodes = episodes,
                Status = Normalize(status),
                Aired = TimePeriodDto.Convert(aired),
                Duration = GetTicks(Normalize(duration)),
                Rating = MapParentalRating(rating),
                Score = (float?)score,
                Synopsis = Normalize(synopsis),
                Broadcast = airing ? AnimeBroadcastDto.From(broadcast) : null,
                Studios = studioNames,
                Genres = genreNames,
                Relations = relations,
                NSFW = genreNames.Any(n => NSFWGenres.Contains(n))
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
