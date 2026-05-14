using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class VoiceActorEntryDto
    {
        public string Language { get; set; }

        public PersonDto Person { get; set; }

        public static VoiceActorEntryDto From(VoiceActorEntry source)
        {
            if (source is null) return null;

            return new VoiceActorEntryDto
            {
                Language = string.IsNullOrWhiteSpace(source.Language) ? null : source.Language,
                Person = PersonDto.From(source.Person)
            };
        }
    }
}
