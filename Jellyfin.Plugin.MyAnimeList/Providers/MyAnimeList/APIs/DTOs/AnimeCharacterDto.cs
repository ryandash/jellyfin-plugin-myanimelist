using System.Collections.Generic;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class AnimeCharacterDto
    {
        public CharacterCacheDto Character { get; set; }
        public string Role { get; set; }
        public List<VoiceActorEntryDto> VoiceActors { get; set; }
    }
}
