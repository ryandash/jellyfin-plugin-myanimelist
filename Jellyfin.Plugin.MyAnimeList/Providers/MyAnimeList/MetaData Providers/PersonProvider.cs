using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
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

        public static PluginConfiguration _config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public string Name => ProviderNames.MyAnimeList;

        public PersonProvider(ILogger<PersonProvider> logger, IHttpClientFactory httpClientFactory)
        {
            _log = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<Person>();
            if (!info.TryGetProviderId(ProviderNames.MyAnimeList, out var id) || !long.TryParse(id, out var malId))
            {
                _log.LogWarning("Metadata Invalid MAL ID {malId} for person with type {type}", id, info.GetType().Name);
                return result;
            }

            PersonSearchResult person = new PersonSearchResult();

            if (_config.SwapVoiceActorsAndCharacters)
            {
                person.character = await JikanAPI.GetCharacterAsync(malId, cancellationToken).ConfigureAwait(false);
                if (person.character is null)
                {
                    return result;
                }

                result.Item = person.ToPerson();
            }
            else
            {
                person.person = await JikanAPI.GetPersonAsync(malId, cancellationToken).ConfigureAwait(false);
                if (person.person is null)
                {
                    return result;
                }

                result.Item = person.ToPerson();
            }

            _log.LogInformation("Metadata Successfully retrieved metadata for person with MAL ID {malId} and type {type}", malId, info.GetType().Name);

            result.HasMetadata = true;

            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            if (!searchInfo.TryGetProviderId(ProviderNames.MyAnimeList, out var id) || !long.TryParse(id, out var malId))
            {
                _log.LogWarning("SearchResults Invalid MAL ID {malId} for person with type {type}", id, searchInfo.GetType().Name);
                return results;
            }

            if (_config.SwapVoiceActorsAndCharacters)
            {

                List<CharacterCacheDto> characters = await JikanAPI.SearchCharacterAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false);
                if (characters is null)
                {
                    return results;
                }

                foreach (var character in characters)
                {
                    results.Add(new RemoteSearchResult()
                    {
                        SearchProviderName = ProviderNames.MyAnimeList,
                        Name = character.Name,
                        ImageUrl = character.Images.Image,
                        ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, character.MalId.ToString() } }
                    });
                }
            }
            else
            {
                List<PersonDto> people = await JikanAPI.SearchPersonAsync(searchInfo.Name, cancellationToken).ConfigureAwait(false);
                if (people is null)
                {
                    return results;
                }

                foreach (var person in people)
                {
                    results.Add(new RemoteSearchResult()
                    {
                        SearchProviderName = ProviderNames.MyAnimeList,
                        Name = person.Name,
                        ImageUrl = person.Images.Image,
                        ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, person.MalId.ToString() } }
                    });
                }
            }

            _log.LogInformation("Search Results Successfully retrieved metadata for person with MAL ID {malId} and type {type}", malId, searchInfo.GetType().Name);

            return results;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient(ProviderNames.MyAnimeList);
            return client.GetAsync(url, cancellationToken);
        }
    }
}
