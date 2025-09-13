using JikanDotNet;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class CharacterCacheDto
    {
        public CharacterEntryDto Character { get; set; }
        public string Role { get; set; }
        public List<VoiceActorEntryDto> VoiceActors { get; set; }

        public static CharacterCacheDto From(AnimeCharacter source)
        {
            if (source == null) return null;

            return new CharacterCacheDto
            {
                Character = CharacterEntryDto.From(source.Character),
                Role = string.IsNullOrWhiteSpace(source.Role) ? null : source.Role,
                VoiceActors = (source.VoiceActors?.Select(VoiceActorEntryDto.From).ToList())
            };
        }
    }
}
