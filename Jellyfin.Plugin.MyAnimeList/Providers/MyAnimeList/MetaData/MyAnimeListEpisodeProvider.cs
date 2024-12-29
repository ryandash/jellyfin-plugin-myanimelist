using JikanDotNet;
using JikanDotNet.Exceptions;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using EpisodeInfo = MediaBrowser.Controller.Providers.EpisodeInfo;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.MetaData
{
    public class MyAnimeListEpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
    {
        private readonly ILogger<MyAnimeListEpisodeProvider> _log;
        private readonly Jikan _jikan;
        private MyAnimeListSearchHelper _searchHelper;

        public int Order => -2;
        public string Name => "MyAnimeList";

        public MyAnimeListEpisodeProvider(ILogger<MyAnimeListEpisodeProvider> logger)
        {
            _log = logger;
            _jikan = JikanSingleton.Instance;
            _searchHelper = new MyAnimeListSearchHelper(_jikan);
        }

        public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken cancellationToken)
        {
            MetadataResult<Episode> result = new MetadataResult<Episode>();
            if (info.Path == null || !info.IndexNumber.HasValue) return result;


            var anime = await GetAnimeInfoAsync(info, cancellationToken).ConfigureAwait(false);
            if (anime == null) return result;
            (var episodeNumber, anime.MalId) = await GetEpisodeNumberAsync(info, anime, cancellationToken).ConfigureAwait(false);

            EpisodeSearchResult episode = new EpisodeSearchResult();
            try
            {
                episode.episode = (await _jikan.GetAnimeEpisodeAsync(anime.MalId.Value, episodeNumber, cancellationToken).ConfigureAwait(false)).Data;
            }
            catch (JikanRequestException)
            {
                // It is normal for some episode data to not exist on myanimelist
            }
            if (episode == null || episode.episode == null) return result;

            anime = (await _jikan.GetAnimeAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false)).Data;
            if (anime == null) return result;

            result.HasMetadata = true;
            result.Item = episode.ToEpisode(anime.Episodes?.ToString().Length ?? 4);
            result.Item.IndexNumber = info.IndexNumber;
            result.Item.ParentIndexNumber = info.ParentIndexNumber.HasValue ? info.ParentIndexNumber.Value : 1;
            result.Provider = ProviderNames.MyAnimeList;
            return result;
        }

        private async Task<JikanDotNet.Anime> GetAnimeInfoAsync(EpisodeInfo info, CancellationToken cancellationToken)
        {
            long? aid = _searchHelper.GetAnimeIdAsync(info, cancellationToken).Result;
            if (!aid.HasValue || info.Path == null || !info.IndexNumber.HasValue) return null;

            _log.LogInformation("Populating Episode Anime info for: {malId}", aid);
            return (await _jikan.GetAnimeAsync(aid.Value, cancellationToken).ConfigureAwait(false)).Data;
        }

        private async Task<(int episodeNumber, long? malID)> GetEpisodeNumberAsync(
    EpisodeInfo info, JikanDotNet.Anime anime, CancellationToken cancellationToken)
        {
            int episodeNumber = info.IndexNumber.Value;

            if (anime.Episodes == null || episodeNumber <= anime.Episodes)
            {
                return (episodeNumber, anime.MalId);
            }

            int tempEpisodeNumber = (int)(episodeNumber - anime.Episodes);
            var malIDPartSeason = info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeListSeason);

            if (!string.IsNullOrEmpty(malIDPartSeason))
            {
                return (tempEpisodeNumber, long.Parse(malIDPartSeason));
            }

            var sequel = (await _jikan.GetAnimeRelationsAsync(anime.MalId.Value, cancellationToken).ConfigureAwait(false))?.Data
                         .FirstOrDefault(r => r.Relation.Equals("Sequel", StringComparison.OrdinalIgnoreCase))?.Entry.FirstOrDefault();

            if (sequel != null)
            {
                info.ProviderIds[ProviderNames.MyAnimeListSeason] = sequel.MalId.ToString();
                return (tempEpisodeNumber, sequel.MalId);
            }

            return (episodeNumber, anime.MalId);
        }

        public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo info, CancellationToken cancellationToken)
        {
            var results = new List<RemoteSearchResult>();
            long? aid = _searchHelper.GetAnimeIdAsync(info, cancellationToken).Result;
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
