using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class CharacterCacheDto
    {
        public long MalId { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public ImagesSetDto Images { get; set; }

        public static CharacterCacheDto From(Character source)
            => source is null ? null : Map(source.MalId, source.Name, source.Url, source.Images);

        public static CharacterCacheDto From(CharacterEntry source)
            => source is null ? null : Map(source.MalId, source.Name, source.Url, source.Images);

        private static CharacterCacheDto Map(long malId, string name, string url, ImagesSet images)
        {
            return new CharacterCacheDto
            {
                MalId = malId,
                Name = string.IsNullOrWhiteSpace(name) ? null : SwapName(name),
                Url = string.IsNullOrWhiteSpace(url) ? null : url,
                Images = ImagesSetDto.From(images),
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
