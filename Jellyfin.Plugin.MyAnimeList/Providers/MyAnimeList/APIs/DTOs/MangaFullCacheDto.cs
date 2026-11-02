using JikanDotNet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class MangaFullCacheDto
    {
        public long? MalId { get; set; }
        public ImagesSetDto Images { get; set; }
        public List<TitleEntryDto> Titles { get; set; }
        public int? Chapters { get; set; }
        public int? Volumes { get; set; }
        public TimePeriodDto Published { get; set; }
        public float? Score { get; set; }
        public string Synopsis { get; set; }
        public string[] Serializations { get; set; }
        public string[] Genres { get; set; }
        public string[] Tags { get; set; }

        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        public static MangaFullCacheDto From(MangaFull source)
        {
            if (source is null || source.MalId <= 0)
                return null;

            var genreNames = source.Genres?
                .Select(g => Normalize(g?.Name))
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .ToArray() ?? Array.Empty<string>();

            var themeNames = (source.Themes ?? Enumerable.Empty<MalUrl>())
                .Concat(source.Demographics ?? Enumerable.Empty<MalUrl>())
                .Concat(source.ExplicitGenres ?? Enumerable.Empty<MalUrl>())
                .Select(g => Normalize(g?.Name))
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .ToArray();

            return new MangaFullCacheDto
            {
                MalId = source.MalId,
                Images = ImagesSetDto.From(source.Images),
                Titles = source.Titles?.Select(TitleEntryDto.From).ToList(),
                Chapters = source.Chapters,
                Volumes = source.Volumes,
                Published = TimePeriodDto.Convert(source.Published),
                Score = (float?)source.Score,
                Synopsis = Normalize(source.Synopsis),
                Serializations = source.Serializations?.Select(s => Normalize(s?.Name)).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() ?? Array.Empty<string>(),
                Genres = genreNames,
                Tags = themeNames
            };
        }

        public static MangaFullCacheDto From(Manga source)
        {
            if (source is null || source.MalId <= 0)
                return null;

            var genreNames = source.Genres?
                .Select(g => Normalize(g?.Name))
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .ToArray() ?? Array.Empty<string>();

            var themeNames = (source.Themes ?? Enumerable.Empty<MalUrl>())
                .Concat(source.Demographics ?? Enumerable.Empty<MalUrl>())
                .Concat(source.ExplicitGenres ?? Enumerable.Empty<MalUrl>())
                .Select(g => Normalize(g?.Name))
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .ToArray();

            return new MangaFullCacheDto
            {
                MalId = source.MalId,
                Images = ImagesSetDto.From(source.Images),
                Titles = source.Titles?.Select(TitleEntryDto.From).ToList(),
                Chapters = source.Chapters,
                Volumes = source.Volumes,
                Published = TimePeriodDto.Convert(source.Published),
                Score = (float?)source.Score,
                Synopsis = Normalize(source.Synopsis),
                Serializations = source.Serializations?.Select(s => Normalize(s?.Name)).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray() ?? Array.Empty<string>(),
                Genres = genreNames,
                Tags = themeNames
            };
        }
    }
}
