using Jellyfin.Plugin.MyAnimeList;
using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList;
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

public abstract class BaseProvider<TItem, TInfo> : IRemoteMetadataProvider<TItem, TInfo> where TItem : BaseItem, IHasLookupInfo<TInfo>, new() where TInfo : ItemLookupInfo, new()
{
    protected readonly ILogger _log;
    protected readonly SearchHelper _searchHelper;
    private readonly IHttpClientFactory _httpClientFactory;

    protected static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public string Name => ProviderNames.MyAnimeList;

    protected BaseProvider(ILogger logger, ILibraryManager libraryManager, IHttpClientFactory httpClientFactory)
    {
        _log = logger;
        _searchHelper = new SearchHelper(libraryManager);
        _httpClientFactory = httpClientFactory;
    }

    protected abstract TItem ConvertToItem(AnimeObject media, TInfo info);

    public virtual async Task<MetadataResult<TItem>> GetMetadata(TInfo info, CancellationToken cancellationToken)
    {
        if (info.Path is null || info is SeasonInfo { IndexNumber: 0 })
        {
            return new MetadataResult<TItem>();
        }

        var anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken, false).ConfigureAwait(false);

        if (anime is null)
        {
            return new MetadataResult<TItem>();
        }

        var media = new AnimeObject
        {
            Anime = anime,
            Characters = await JikanAPI.GetAnimeCharactersAsync(anime.MalId!.Value, cancellationToken).ConfigureAwait(false)
        };

        if (info is SeasonInfo)
        {
            _ = await JikanAPI.GetAnimeEpisodesAsync(anime.MalId!.Value, cancellationToken).ConfigureAwait(false);
        }

        var result = new MetadataResult<TItem>
        {
            HasMetadata = true,
            Item = ConvertToItem(media, info),
            Provider = Name
        };

        if (ShouldLoadPeople(info))
        {
            result.People = media.GetPeopleInfo();
        }

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(TInfo info, CancellationToken cancellationToken)
    {
        var anime = await _searchHelper.GetAnimeAsync(_log, info, cancellationToken, true).ConfigureAwait(false);

        if (anime is null)
        {
            return [];
        }

        return
        [
            new AnimeSearchResult
            {
                Anime = anime
            }.ToSearchResult()
        ];
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        return _httpClientFactory.CreateClient(ProviderNames.MyAnimeList).GetAsync(url, cancellationToken);
    }

    private static bool ShouldLoadPeople(ItemLookupInfo info)
    {
        return info switch
        {
            SeriesInfo => Config.SeriesMetadata.People,
            SeasonInfo => Config.SeasonMetadata.People,
            MovieInfo => Config.MovieMetadata.People,
            _ => false
        };
    }
}
