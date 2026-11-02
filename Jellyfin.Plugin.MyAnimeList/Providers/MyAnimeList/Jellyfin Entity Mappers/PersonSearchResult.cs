using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Person = MediaBrowser.Controller.Entities.Person;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class PersonSearchResult : JellyfinEntityMapperBase
    {
        public CharacterCacheDto Character { get; set; }
        public PersonDto Person { get; set; }

        internal Person ToPerson(PersonCreditType type)
        {
            return type switch
            {
                PersonCreditType.Characters when Character != null => new Person
                {
                    Name = Character.Name,
                    Overview = Character.Description,
                    ProviderIds =
                    {
                        { ProviderNames.MyAnimeList, Character.Url }
                    }
                },
                PersonCreditType.VoiceActors when Person != null => new Person
                {
                    Name = Person.Name,
                    Overview = Person.Description,
                    ProviderIds =
                    {
                        { ProviderNames.MyAnimeList, Person.Url }
                    }
                },
                _ => null
            };
        }
    }
}
