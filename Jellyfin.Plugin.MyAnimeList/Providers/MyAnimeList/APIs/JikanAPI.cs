using JikanDotNet;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs
{
    public static class JikanSingleton
    {
        private static readonly Lazy<Jikan> _jikanInstance = new Lazy<Jikan>(() => new Jikan());
        private static Jikan Instance => _jikanInstance.Value;

        // Simple in-memory cache
        private static readonly ConcurrentDictionary<string, (DateTime Expiry, object Value)> _cache
            = new ConcurrentDictionary<string, (DateTime, object)>();

        private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(1);

        private static string GetKey(string method, object param) => $"{method}:{param}";

        private static async Task<T> GetOrAddAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan? ttl = null)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.Expiry > DateTime.UtcNow && entry.Value is T cached)
                {
                    return cached;
                }
                _cache.TryRemove(key, out _);
            }

            var result = await factory().ConfigureAwait(false);
            _cache[key] = (DateTime.UtcNow + (ttl ?? DefaultTtl), result);
            return result;
        }

        // Basic wrappers
        public static Task<BaseJikanResponse<JikanDotNet.Anime>> GetAnimeAsync(long malId, CancellationToken token)
        {
            return GetOrAddAsync(
                GetKey("GetAnime", malId),
                () => Instance.GetAnimeAsync(malId, token));
        }

        public static Task<PaginatedJikanResponse<ICollection<RelatedEntry>>> GetAnimeRelationsAsync(long malId, CancellationToken token)
        {
            return GetOrAddAsync(
                GetKey("GetAnimeRelations", malId),
                () => Instance.GetAnimeRelationsAsync(malId, token));
        }

        public static Task<BaseJikanResponse<AnimeEpisode>> GetAnimeEpisodeAsync(long malId, int episodeNumber, CancellationToken cancellationToken)
        {
            return GetOrAddAsync(
                GetKey("GetAnimeEpisode", $"{malId}:{episodeNumber}"),
                () => Instance.GetAnimeEpisodeAsync(malId, episodeNumber, cancellationToken)
            );
        }

        public static Task<PaginatedJikanResponse<ICollection<JikanDotNet.Anime>>> SearchAnimeAsync(string searchTerm, CancellationToken cancellationToken)
        {
            return Instance.SearchAnimeAsync(searchTerm, cancellationToken);
        }

        public static Task<BaseJikanResponse<ICollection<AnimeCharacter>>> GetAnimeCharactersAsync(long malId, CancellationToken cancellationToken)
        {
            return GetOrAddAsync(
                GetKey("GetAnimeCharacters", malId),
                () => Instance.GetAnimeCharactersAsync(malId, cancellationToken)
            );
        }

        public static Task<BaseJikanResponse<ICollection<ImagesSet>>> GetAnimePicturesAsync(long malId, CancellationToken cancellationToken)
        {
            return GetOrAddAsync(
                GetKey("GetAnimePictures", malId),
                () => Instance.GetAnimePicturesAsync(malId, cancellationToken)
            );
        }

    }
}
