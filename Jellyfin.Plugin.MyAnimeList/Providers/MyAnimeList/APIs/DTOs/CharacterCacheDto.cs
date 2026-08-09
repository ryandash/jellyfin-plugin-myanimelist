using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class CharacterCacheDto
    {
        public long MalId { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public ImagesSetDto Images { get; set; }
        public string Description { get; set; }

        public bool HasFullDetails => !string.IsNullOrWhiteSpace(Description);

        public static CharacterCacheDto From(Character source)
            => source is null ? null :
            Map(
                source.MalId,
                source.Name,
                source.Url,
                source.Images,
                source.About
            );

        public static CharacterCacheDto From(CharacterEntry source)
            => source is null ? null :
            Map(
                source.MalId,
                source.Name,
                source.Url,
                source.Images,
                null
            );

        private static string SwapName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            if (!input.Contains(',')) return input;
            var parts = input.Split(',');
            return parts.Length == 2
                ? $"{parts[1].Trim()} {parts[0].Trim()}"
                : input.Trim();
        }

        private static CharacterCacheDto Map(long malId, string name, string url, ImagesSet images, string description)
            => new CharacterCacheDto
            {
                MalId = malId,
                Name = SwapName(name),
                Url = string.IsNullOrWhiteSpace(url) ? null : url,
                Images = ImagesSetDto.From(images),
                Description = description
            };

        public static CharacterCacheDto MergeCharacterDetails(CharacterCacheDto existing, CharacterCacheDto detailed)
        {
            if (existing is null)
                return detailed;
            if (detailed is null)
                return existing;
            if (existing.MalId <= 0)
                existing.MalId = detailed.MalId;
            if (string.IsNullOrWhiteSpace(existing.Name))
                existing.Name = detailed.Name;
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
