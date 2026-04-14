using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class CharacterCacheDto
    {
        public long MalId { get; set; }

        public string Url { get; set; }

        public ImagesSetDto Images { get; set; }
        public string Name { get; set; }

        public static CharacterCacheDto From(CharacterEntry source)
        {
            if (source == null) return null;

            return new CharacterCacheDto
            {
                MalId = source.MalId,
                Url = string.IsNullOrWhiteSpace(source.Url) ? null : source.Url,
                Images = ImagesSetDto.From(source.Images),
                Name = string.IsNullOrWhiteSpace(source.Name) ? null : source.Name
            };
        }

        public static CharacterCacheDto From(Character source)
        {
            if (source == null) return null;

            return new CharacterCacheDto
            {
                MalId = source.MalId,
                Url = string.IsNullOrWhiteSpace(source.Url) ? null : source.Url,
                Images = ImagesSetDto.From(source.Images),
                Name = string.IsNullOrWhiteSpace(source.Name) ? null : source.Name
            };
        }
    }
}
