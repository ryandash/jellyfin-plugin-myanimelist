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

        public string Name => ProviderNames.MyAnimeList;

        protected MyAnimeListBaseProvider(ILogger logger, ILibraryManager libraryManager)
        {
            _log = logger;
            var plugin = Plugin.Instance;
            _searchHelper = new MyAnimeListSearchHelper(libraryManager, plugin.GetHttpClient(), plugin.Configuration);
        }

        protected abstract TItem ConvertToItem(AnimeObject media);

        public virtual async Task<MetadataResult<TItem>> GetMetadata(TInfo info, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<TItem>();
            if (info.Path == null || (info is SeasonInfo && info.IndexNumber == 0))
                return result;

            var anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken, false).ConfigureAwait(false);
            if (anime == null)
                return result;

            var media = new AnimeObject
            {
                anime = anime,
                characters = await JikanAPI.GetAnimeCharactersAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false)
            };

            result.HasMetadata = true;
            result.Item = ConvertToItem(media);
            result.People = media.GetPeopleInfo();
            result.Provider = Name;

            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(TInfo info, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();

            var anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken, true).ConfigureAwait(false);
            if (anime == null)
                return results;

            var searchResult = new AnimeSearchResult { anime = anime };
            results.Add(searchResult.ToSearchResult());

            return results;
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return Plugin.Instance.GetHttpClient().GetAsync(url, cancellationToken);
        }
    }
}
