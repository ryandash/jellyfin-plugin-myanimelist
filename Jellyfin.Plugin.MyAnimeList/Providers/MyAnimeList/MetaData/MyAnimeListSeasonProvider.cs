using JikanDotNet;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Season = MediaBrowser.Controller.Entities.TV.Season;
using SeasonInfo = MediaBrowser.Controller.Providers.SeasonInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListSeasonProvider : IRemoteMetadataProvider<Season, SeasonInfo>, IHasOrder
    {
        private readonly ILogger<MyAnimeListSeasonProvider> _log;
        private readonly Jikan _jikan;
        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListSeasonProvider(ILogger<MyAnimeListSeasonProvider> logger)
        {
            _log = logger;
            _jikan = JikanSingleton.Instance;
        }

        public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken cancellationToken)
        {
            MetadataResult<Season> result = new MetadataResult<Season>();
            if (info.Path == null || !info.IndexNumber.HasValue) return result;

            long? malId = MyAnimeListSearchHelper.GetAnimeIdAsync(info, cancellationToken).Result;
            if (!malId.HasValue) return result;
            Anime media = await GetAnimeInfoAsync(malId.Value, info, cancellationToken).ConfigureAwait(false);
            if (media.anime == null) return result;

            result.HasMetadata = true;
            result.Item = media.ToSeason();
            result.People = media.GetPeopleInfo();
            result.Provider = ProviderNames.MyAnimeList;
            return result;
        }

        private async Task<Anime> GetAnimeInfoAsync(long malId, SeasonInfo info, CancellationToken cancellationToken)
        {
            _log.LogInformation("Populating Season metadata for: {straid}", malId);
            Anime media = new Anime
            {
                anime = (await _jikan.GetAnimeAsync(malId, cancellationToken).ConfigureAwait(false)).Data,
                characters = (await _jikan.GetAnimeCharactersAsync(malId, cancellationToken).ConfigureAwait(false)).Data
            };
            return media;
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo info, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            long? aid = MyAnimeListSearchHelper.GetAnimeIdAsync(info, cancellationToken).Result;
            if (aid.HasValue)
            {
                AnimeSearchResult aid_result = new AnimeSearchResult();
                aid_result.anime = (await _jikan.GetAnimeAsync(aid.Value, cancellationToken).ConfigureAwait(false)).Data;
                if (aid_result.anime != null)
                {
                    results.Add(aid_result.ToSearchResult());
                }
            }

            return results;
        }

        public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        {
            var httpClient = Plugin.Instance.GetHttpClient();
            return await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
        }
    }
}
