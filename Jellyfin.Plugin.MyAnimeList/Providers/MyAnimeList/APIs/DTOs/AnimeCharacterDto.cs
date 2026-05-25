using JikanDotNet;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class AnimeCharacterDto
    {
        public CharacterCacheDto Character { get; set; }
        public string Role { get; set; }
        public List<VoiceActorEntryDto> VoiceActors { get; set; }

        public static AnimeCharacterDto From(AnimeCharacter source)
        {
            if (source is null) return null;

            return new AnimeCharacterDto
            {
                Character = CharacterCacheDto.From(source.Character),
                Role = source.Role,
                VoiceActors = source.VoiceActors.Select(VoiceActorEntryDto.From).ToList()
            };
        }
    }
}
