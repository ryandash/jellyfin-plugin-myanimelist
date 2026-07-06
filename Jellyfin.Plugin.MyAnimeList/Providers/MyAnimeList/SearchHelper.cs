using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.Helpers;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class SearchHelper
    {
        private NameBuilder nameBuilder;

        private static PluginConfiguration _config => Plugin.Instance?.Configuration ?? new PluginConfiguration
        {
            EnableDebug = true,
            EnableBestAttempt = true
        };

        public SearchHelper(ILibraryManager libraryManager)
        {
            nameBuilder = new NameBuilder(libraryManager);
        }

        public async Task<AnimeFullCacheDto> GetAnimeAsync(ILogger _log, ItemLookupInfo info, CancellationToken cancellationToken, bool SearchResult)
        {

            bool debug = _config.EnableDebug;
            bool forceNew = _config.ForceNewMetadata;

            long? malId = MalIdResolver.TryGetDirectMalId(info);

            if (malId.HasValue && (!forceNew || SearchResult))
            {
                if (debug) _log.LogInformation("Returned malID: {malID} for type {type}", malId, info.GetType().ToString());
                return (await JikanAPI.GetAnimeFullAsync(malId.Value, cancellationToken).ConfigureAwait(false));
            }

            if (debug)
            {
                _log.LogInformation("Original path: {path}", info.Path);
                _log.LogInformation("Original name: {name}", info.Name);
            }

            (string searchTerm, bool specialEpisode) = nameBuilder.GetSearchName(info, _log, debug);

            int? confidence = null;

            if (!specialEpisode) malId ??= MalIdResolver.TryGetSeriesMalId(info, out confidence);

            if (!malId.HasValue && !string.IsNullOrWhiteSpace(searchTerm))
            {
                malId = MalIdResolver.ExtractMalIdFromSearchName(searchTerm);
                if (malId.HasValue)
                {
                    confidence = 100;
                    if (debug) _log.LogInformation("Extracted MAL ID {MalId} from name", malId);
                }
            }

            string searchName = (!string.IsNullOrWhiteSpace(info.Name) && !forceNew) ? info.Name : searchTerm;

            if (!malId.HasValue && !string.IsNullOrWhiteSpace(searchName))
            {
                if (debug) _log.LogInformation("Search using name: {name}", searchName);
                (malId, int similarity) = await SearchService.SearchForBestAnimeID(_log, searchName, info is MovieInfo, specialEpisode, _config, cancellationToken).ConfigureAwait(false);
                if (malId.HasValue)
                {
                    confidence = similarity;
                }
                else if (debug) _log.LogError("Could not find MalID using Name: {name}", searchName);
            }

            if (!malId.HasValue)
            {
                if (debug) _log.LogError("Failed to Find MAL ID");
                return null;
            }

            if (debug) _log.LogInformation("Found MalID: {MalId}", malId);

            int seasonNumber = info switch
            {
                EpisodeInfo e => e.ParentIndexNumber ?? 1,
                _ => info.IndexNumber ?? 1
            };

            if (info is MovieInfo)
            {
                return await JikanAPI.GetAnimeFullAsync(malId.Value, cancellationToken).ConfigureAwait(false);
            }

            int conf = confidence ?? 100;

            return await RelationsResolver.GetCurrentAnimeSeasonAsync(_log, malId.Value, conf, seasonNumber, cancellationToken).ConfigureAwait(false);
        }
    }
}
