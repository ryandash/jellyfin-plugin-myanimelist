using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class VoiceActorEntryCacheDto
    {
        public string Language { get; set; }

        public long PersonId { get; set; }

        public static VoiceActorEntryCacheDto From(VoiceActorEntry source)
        {
            if (source is null) return null;

            return new VoiceActorEntryCacheDto
            {
                Language = string.IsNullOrWhiteSpace(source.Language) ? null : source.Language,
                PersonId = source.Person.MalId
            };
        }
    }
}
