using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.ExternalIds;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData_Providers
{
    public class PersonProvider : IRemoteMetadataProvider<Person, PersonLookupInfo>
    {
        private readonly ILogger _log;
        private readonly IHttpClientFactory _httpClientFactory;

        private static PluginConfiguration _config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public string Name => ProviderNames.MyAnimeList;

        public PersonProvider(ILogger<PersonProvider> logger, IHttpClientFactory httpClientFactory)
        {
            _log = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Person>
            {
                HasMetadata = false
            };

            var enableDebug = _config.EnableDebug;

            if (!info.TryGetProviderId(ProviderNames.MyAnimeList, out var id))
            {
                if (enableDebug) _log.LogWarning("Missing MAL ID for person with type {type}", info.GetType().Name);

                return result;
            }

            if (!ExternalUrlProvider.TryExtractPersonId(id, out var malId, out var personType))
            {
                if (enableDebug) _log.LogWarning("Invalid MAL ID {malId} for person with type {type}", id, info.GetType().Name);

                return result;
            }

            if (personType == PersonCreditType.Characters)
            {
                var character = await JikanAPI.GetCharacterAsync(malId, cancellationToken).ConfigureAwait(false);

                if (character is null)
                    return result;

                result.Item = new PersonSearchResult
                {
                    Character = character
                }.ToPerson(PersonCreditType.Characters);
            }
            else
            {
                var person = await JikanAPI.GetPersonAsync(malId, cancellationToken).ConfigureAwait(false);

                if (person is null)
                    return result;

                result.Item = new PersonSearchResult
                {
                    Person = person
                }.ToPerson(PersonCreditType.VoiceActors);
            }

            if (result.Item is null)
                return result;

            result.HasMetadata = true;

            if (enableDebug) _log.LogInformation("Successfully retrieved metadata for MAL ID {malId} as {personType} for type {type}", malId, personType, info.GetType().Name);

            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
        {
            if (searchInfo.TryGetProviderId(ProviderNames.MyAnimeList, out var id) && ExternalUrlProvider.TryExtractPersonId(id, out var malId, out var personType))
            {
                var result = await GetById(malId, personType, cancellationToken).ConfigureAwait(false);

                return result is null ? [] : [result];
            }

            return _config.PersonCreditPreference switch
            {
                PersonCreditType.Characters => await SearchCharacters(searchInfo.Name, cancellationToken).ConfigureAwait(false),

                PersonCreditType.VoiceActors => await SearchPeople(searchInfo.Name, cancellationToken).ConfigureAwait(false),

                PersonCreditType.Both => await SearchBoth(searchInfo.Name, cancellationToken).ConfigureAwait(false),

                _ => []
            };
        }

        private async Task<IEnumerable<RemoteSearchResult>> SearchBoth(string name, CancellationToken cancellationToken)
        {
            var charactersTask = SearchCharacters(name, cancellationToken);
            var peopleTask = SearchPeople(name, cancellationToken);

            await Task.WhenAll(charactersTask, peopleTask).ConfigureAwait(false);

            return
            [
                .. await charactersTask.ConfigureAwait(false),
                .. await peopleTask.ConfigureAwait(false)
            ];
        }

        private async Task<RemoteSearchResult> GetById(long malId, PersonCreditType personType, CancellationToken cancellationToken)
        {
            if (personType == PersonCreditType.Characters)
            {
                var character = await JikanAPI.GetCharacterAsync(malId, cancellationToken).ConfigureAwait(false);

                return character is null ? null : CreateResult(character.Name, character.Images?.Image, character.Url);
            }

            var person = await JikanAPI.GetPersonAsync(malId, cancellationToken).ConfigureAwait(false);

            return person is null ? null : CreateResult(person.Name, person.Images?.Image, person.Url);
        }

        private async Task<IEnumerable<RemoteSearchResult>> SearchCharacters(string name, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            var characters = await JikanAPI.SearchCharacterAsync(name, cancellationToken).ConfigureAwait(false);

            if (characters is null)
                return results;

            foreach (var character in characters)
            {
                results.Add(CreateResult(
                    character.Name,
                    character.Images?.Image,
                    character.MalId.ToString()));
            }

            return results;
        }

        private async Task<IEnumerable<RemoteSearchResult>> SearchPeople(string name, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            var people = await JikanAPI.SearchPersonAsync(name, cancellationToken).ConfigureAwait(false);

            if (people is null)
                return results;

            foreach (var person in people)
            {
                results.Add(CreateResult(person.Name, person.Images?.Image, person.MalId.ToString()));
            }

            return results;
        }

        private static RemoteSearchResult CreateResult(string name, string imageUrl, string malUrl)
        {
            return new RemoteSearchResult
            {
                SearchProviderName = ProviderNames.MyAnimeList,
                Name = name,
                ImageUrl = imageUrl,
                ProviderIds = new Dictionary<string, string>
                {
                    { ProviderNames.MyAnimeList, malUrl }
                }
            };
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient(ProviderNames.MyAnimeList);
            return client.GetAsync(url, cancellationToken);
        }
    }
}
