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

            return Map(
                source.MalId,
                source.Name,
                source.Title,
                source.Url,
                source.Images
            );
        }

        public static PersonDto From(Person source)
        {
            if (source is null) return null;

            return Map(
                source.MalId,
                source.Name,
                null, // Person has no Title
                source.Url,
                source.Images
            );
        }

        private static PersonDto Map(long malId, string name, string title, string url, ImagesSet images)
        {
            return new PersonDto
            {
                MalId = malId,
                Name = string.IsNullOrWhiteSpace(name) ? null : SwapName(name),
                Title = string.IsNullOrWhiteSpace(title) ? null : title,
                Url = string.IsNullOrWhiteSpace(url) ? null : url,
                Images = ImagesSetDto.From(images)
            };
        }

        private static string SwapName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            var parts = input.Split(',');
            return parts.Length == 2
                ? $"{parts[1].Trim()} {parts[0].Trim()}"
                : input.Trim();
        }
    }
}
