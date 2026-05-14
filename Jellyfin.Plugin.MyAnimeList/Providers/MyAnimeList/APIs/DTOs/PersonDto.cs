using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class PersonDto
    {
        public long MalId { get; set; }

        public string Name { get; set; }

        public string Title { get; set; }

        public string Url { get; set; }

        public ImagesSetDto Images { get; set; }

        public static PersonDto From(MalImageSubItem source)
        {
            if (source is null) return null;

            return new PersonDto
            {
                MalId = source.MalId,
                Name = string.IsNullOrWhiteSpace(source.Name) ? null : source.Name,
                Title = string.IsNullOrWhiteSpace(source.Title) ? null : source.Title,
                Url = string.IsNullOrWhiteSpace(source.Url) ? null : source.Url,
                Images = ImagesSetDto.From(source.Images)
            };
        }

        public static PersonDto From(Person source)
        {
            if (source is null) return null;

            return new PersonDto
            {
                MalId = source.MalId,
                Name = string.IsNullOrWhiteSpace(source.Name) ? null : source.Name,
                Url = string.IsNullOrWhiteSpace(source.Url) ? null : source.Url,
                Images = ImagesSetDto.From(source.Images)
            };
        }
    }
}
