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
    public class MangaProvider : IRemoteMetadataProvider<Book, BookInfo>
    {
        private readonly ILogger<MangaProvider> _logger;
        private readonly SearchHelper _searchHelper;
        private readonly IHttpClientFactory _httpClientFactory;
        protected static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

        public string Name => ProviderNames.MyAnimeList;

        public MangaProvider(ILogger<MangaProvider> logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _searchHelper = new SearchHelper(libraryManager);
            _httpClientFactory = httpClientFactory;
        }

        public async Task<MetadataResult<Book>> GetMetadata(BookInfo info, CancellationToken cancellationToken)
        {
            if (info.Path is null)
            {
                return new MetadataResult<Book>();
            }

            var manga = await _searchHelper.GetMangaAsync(_logger, info, cancellationToken, false).ConfigureAwait(false);

            if (manga is null)
            {
                return new MetadataResult<Book>();
            }

            var media = new MangaObject
            {
                Manga = manga,
                Characters = await JikanAPI.GetMangaCharactersAsync(manga.MalId!.Value, cancellationToken).ConfigureAwait(false)
            };

            var result = new MetadataResult<Book>
            {
                HasMetadata = true,
                Item = media.ToManga(info),
                Provider = Name
            };

            if (Config.BookMetadata.People)
            {
                result.People = media.GetPeopleInfo();
            }

            return result;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(BookInfo info, CancellationToken cancellationToken)
        {
            var manga = await _searchHelper.GetMangaAsync(_logger, info, cancellationToken, true).ConfigureAwait(false);

            if (manga is null)
            {
                return [];
            }

            return
            [
                new MangaSearchResult
                {
                    Manga = manga
                }.ToSearchResult()
            ];
        }

        public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            return _httpClientFactory.CreateClient(ProviderNames.MyAnimeList).GetAsync(url, cancellationToken);
        }
    }
}
