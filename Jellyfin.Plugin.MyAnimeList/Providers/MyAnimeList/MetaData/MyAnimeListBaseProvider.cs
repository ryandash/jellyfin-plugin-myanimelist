using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public abstract class MyAnimeListBaseProvider<TItem, TInfo> : IRemoteMetadataProvider<TItem, TInfo>
        where TItem : BaseItem, IHasLookupInfo<TInfo>, new()
        where TInfo : ItemLookupInfo, new()
    {
        protected readonly ILogger _log;
        protected readonly MyAnimeListSearchHelper _searchHelper;
        private readonly IHttpClientFactory _httpClientFactory;

        public string Name => ProviderNames.MyAnimeList;

        protected MyAnimeListBaseProvider(ILogger logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory)
        {
            _log = logger;
            _searchHelper = new MyAnimeListSearchHelper(libraryManager, httpClientFactory);
            _httpClientFactory = httpClientFactory;
        }

        protected abstract TItem ConvertToItem(AnimeObject media, ItemLookupInfo info);

        public virtual async Task<MetadataResult<TItem>> GetMetadata(TInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<TItem>();
            result.HasMetadata = true;
            result.Item = new TItem
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber,
                Name = info.Name,
                OriginalTitle = info.OriginalTitle
            };

            if (info.Path is null || (info is SeasonInfo && info.IndexNumber == 0))
                return result;

            var anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken, false).ConfigureAwait(false);
            if (anime is null)
                return result;

            var media = new AnimeObject
            {
                anime = anime,
                characters = await JikanAPI.GetAnimeCharactersAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false),
            };

            if (info is SeasonInfo)
            {
                _ = await JikanAPI.GetAnimeEpisodesAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false);
            }

            result.Item = ConvertToItem(media, info);
            result.People = media.GetPeopleInfo();
            result.Provider = Name;

            return result;
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
