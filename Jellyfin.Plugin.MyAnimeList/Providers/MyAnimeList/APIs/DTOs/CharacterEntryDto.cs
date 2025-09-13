using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class CharacterEntryDto
    {
        public long MalId { get; set; }

        public string Name { get; set; }

        public string Url { get; set; }

        public ImagesSetDto Images { get; set; }

        public static CharacterEntryDto From(CharacterEntry source)
        {
            if (source == null) return null;

            return new CharacterEntryDto
            {
                MalId = source.MalId,
                Url = string.IsNullOrWhiteSpace(source.Url) ? null : source.Url,
                Name = string.IsNullOrWhiteSpace(source.Name) ? null : source.Name,
                Images = ImagesSetDto.From(source.Images)
            };
        }
    }
}
