using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public abstract class BaseProvider<TItem, TInfo> : IRemoteMetadataProvider<TItem, TInfo>
        where TItem : BaseItem, IHasLookupInfo<TInfo>, new()
        where TInfo : ItemLookupInfo, new()
    {
        protected readonly ILogger _log;
        protected readonly SearchHelper _searchHelper;
        private readonly IHttpClientFactory _httpClientFactory;
        public static PluginConfiguration _config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public string Name => ProviderNames.MyAnimeList;

        protected BaseProvider(ILogger logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory)
        {
            _log = logger;
            _searchHelper = new SearchHelper(libraryManager);
            _httpClientFactory = httpClientFactory;
        }

        protected abstract TItem ConvertToItem(AnimeObject media, ItemLookupInfo info);

        public virtual async Task<MetadataResult<TItem>> GetMetadata(TInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<TItem>
            {
                HasMetadata = false
            };

            if (info.Path is null || (info is SeasonInfo && info.IndexNumber == 0))
            {
                return result;
            }

            var anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken, false).ConfigureAwait(false);
            if (anime is null)
            {
                return result;
            }


            var media = new AnimeObject
            {
                anime = anime,
                characters = await JikanAPI.GetAnimeCharactersAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false),
            };

            if (info is SeasonInfo)
            {
                _ = await JikanAPI.GetAnimeEpisodesAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false);
            }

            return new MetadataResult<TItem>
            {
                HasMetadata = true,
                Item = ConvertToItem(media, info),
                People = media.GetPeopleInfo(),
                Provider = Name
            };
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(TInfo info, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            var anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken, true).ConfigureAwait(false);
            if (anime is null)
                return results;

            var searchResult = new AnimeSearchResult { anime = anime };
            results.Add(searchResult.ToSearchResult());

            return results;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient(ProviderNames.MyAnimeList);
            return client.GetAsync(url, cancellationToken);
        }
    }
}
