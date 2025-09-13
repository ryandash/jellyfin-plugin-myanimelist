using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class AnimeCacheDto
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
        public List<MalUrlDto> Studios { get; set; }
        public List<MalUrlDto> Genres { get; set; }
        public string Synopsis { get; set; }

        public static AnimeCacheDto From(JikanDotNet.Anime source)
        {
            if (source == null) return null;

            return new AnimeCacheDto
            {
                MalId = source.MalId ?? 0,
                Url = source.Url,
                Titles = source.Titles?.Select(TitleEntryDto.From).ToList(),
                Images = ImagesSetDto.From(source.Images),
                Aired = TimePeriodDto.Convert(source.Aired),
                Duration = source.Duration,
                Score = source.Score,
                Episodes = source.Episodes,
                Type = source.Type,
                Status = source.Status,
                Studios = source.Studios?.Select(MalUrlDto.From).ToList(),
                Genres = source.Genres?.Select(MalUrlDto.From).ToList(),
                Synopsis = string.IsNullOrWhiteSpace(source.Synopsis) ? null : source.Synopsis
            };
        }
    }
}
