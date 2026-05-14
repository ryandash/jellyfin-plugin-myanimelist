using System.Collections.Generic;
using System.Linq;
using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class AnimeCharacterIdCacheDto
    {
        public long CharacterId { get; set; }
        public string Role { get; set; }
        public List<VoiceActorEntryDto> VoiceActors { get; set; }

        public static AnimeCharacterIdCacheDto From(AnimeCharacter source)
        {
            if (source is null) return null;

            return new AnimeCharacterIdCacheDto
            {
                CharacterId = source.Character.MalId,
                Role = string.IsNullOrWhiteSpace(source.Role) ? null : source.Role,
                VoiceActors = source.VoiceActors?.Select(VoiceActorEntryDto.From).ToList()
            };
        }
    }
}
