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

        public string Description { get; set; }

        public bool HasFullDetails => !string.IsNullOrWhiteSpace(Description);

        public static PersonDto From(MalImageSubItem source)
        {
            if (source is null) return null;

            return Map(
                source.MalId,
                source.Name,
                source.Title,
                source.Url,
                source.Images,
                null
            );
        }

        public static PersonDto From(Person source)
        {
            if (source is null) return null;

            return Map(
                source.MalId,
                source.Name,
                null,
                source.Url,
                source.Images,
                source.About
            );
        }

        private static PersonDto Map(long malId, string name, string title, string url, ImagesSet images, string description)
        {
            return new PersonDto
            {
                MalId = malId,
                Name = string.IsNullOrWhiteSpace(name) ? null : name.Replace(",", ""),
                Title = string.IsNullOrWhiteSpace(title) ? null : title,
                Url = string.IsNullOrWhiteSpace(url) ? null : url,
                Images = ImagesSetDto.From(images),
                Description = string.IsNullOrWhiteSpace(description) ? null : description
            };
        }

        public static PersonDto MergePersonDetails(PersonDto existing, PersonDto detailed)
        {
            if (existing is null)
                return detailed;
            if (detailed is null)
                return existing;
            if (existing.MalId <= 0)
                existing.MalId = detailed.MalId;
            if (string.IsNullOrWhiteSpace(existing.Name))
                existing.Name = detailed.Name;
            if (string.IsNullOrWhiteSpace(existing.Title))
                existing.Title = detailed.Title;
            if (string.IsNullOrWhiteSpace(existing.Url))
                existing.Url = detailed.Url;
            if (existing.Images is null)
                existing.Images = detailed.Images;
            if (string.IsNullOrWhiteSpace(existing.Description))
                existing.Description = detailed.Description;
            return existing;
        }
    }
}
