using System.Collections.Generic;
using System.Linq;
using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class AnimeFullCacheDto
    {
        public long? MalId { get; set; }
        public string Url { get; set; }
        public List<TitleEntryDto> Titles { get; set; }
        public ImagesSetDto Images { get; set; }
        public TimePeriodDto Aired { get; set; }
        public string Duration { get; set; }
        public double? Score { get; set; }
        public int? Episodes { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string[] Studios { get; set; }
        public string[] Genres { get; set; }
        public string Synopsis { get; set; }
        public List<RelatedEntryDto> Relations { get; set; }

        private static string Normalize(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        public static AnimeFullCacheDto From(AnimeFull source)
        {
            if (source == null || !source.MalId.HasValue) return null;

            return new AnimeFullCacheDto
            {
                MalId = source.MalId,
                Url = Normalize(source.Url),
                Titles = source.Titles?.Select(TitleEntryDto.From).ToList(),
                Images = ImagesSetDto.From(source.Images),
                Aired = TimePeriodDto.Convert(source.Aired),
                Duration = Normalize(source.Duration),
                Score = source.Score,
                Episodes = source.Episodes,
                Type = Normalize(source.Type),
                Status = Normalize(source.Status),
                Studios = source.Studios != null && source.Studios.Any()
            ? source.Studios.Select(s => Normalize(s.Name)).Where(n => n != null).ToArray()
            : null,
                Genres = source.Genres != null && source.Genres.Any()
            ? source.Genres.Select(g => Normalize(g.Name)).Where(n => n != null).ToArray()
            : null,
                Synopsis = Normalize(source.Synopsis),
                Relations = RelatedEntryDto.FilterRelations(source.Relations),
            };
        }

        public static AnimeFullCacheDto From(Anime source)
        {
            if (source == null || !source.MalId.HasValue) return null;

            return new AnimeFullCacheDto
            {
                MalId = source.MalId,
                Url = Normalize(source.Url),
                Titles = source.Titles?.Select(TitleEntryDto.From).ToList(),
                Images = ImagesSetDto.From(source.Images),
                Aired = TimePeriodDto.Convert(source.Aired),
                Duration = Normalize(source.Duration),
                Score = source.Score,
                Episodes = source.Episodes,
                Type = Normalize(source.Type),
                Status = Normalize(source.Status),
                Studios = source.Studios != null && source.Studios.Any()
                    ? source.Studios.Select(s => Normalize(s.Name)).Where(n => n != null).ToArray()
                    : null,
                Genres = source.Genres != null && source.Genres.Any()
                    ? source.Genres.Select(g => Normalize(g.Name)).Where(n => n != null).ToArray()
                    : null,
                Synopsis = Normalize(source.Synopsis),
                Relations = null,
            };
        }
    }
}
