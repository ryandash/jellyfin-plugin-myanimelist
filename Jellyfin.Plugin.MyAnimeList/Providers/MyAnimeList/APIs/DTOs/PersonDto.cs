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

        public static PersonDto From(MalImageSubItem source, bool legacyJikan)
            => source is null ? null :
            Map(
                source.MalId,
                source.Name,
                source.Title,
                source.Url,
                source.Images,
                null,
                legacyJikan
            );


        public static PersonDto From(Person source, bool legacyJikan)
            => source is null ? null :
            Map(
                source.MalId,
                source.Name,
                null,
                source.Url,
                source.Images,
                source.About,
                legacyJikan
            );

        private static string SwapName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            var parts = input.Split(',');
            return parts.Length == 2
                ? $"{parts[1].Trim()} {parts[0].Trim()}"
                : input.Trim();
        }

        private static PersonDto Map(long malId, string name, string title, string url, ImagesSet images, string description, bool legacyJikan)
            => new PersonDto
            {
                MalId = malId,
                Name = string.IsNullOrWhiteSpace(name)
                    ? null
                    : legacyJikan
                        ? SwapName(name)
                        : name.Replace(",", ""),
                Title = string.IsNullOrWhiteSpace(title) ? null : title,
                Url = string.IsNullOrWhiteSpace(url) ? null : url,
                Images = ImagesSetDto.From(images),
                Description = string.IsNullOrWhiteSpace(description) ? null : description
            };

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
