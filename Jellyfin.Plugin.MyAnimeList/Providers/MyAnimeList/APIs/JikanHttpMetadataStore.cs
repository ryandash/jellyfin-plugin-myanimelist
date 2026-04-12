using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs
{
    public static class JikanHttpMetadataStore
    {
        private static readonly ConcurrentDictionary<string, DateTime> _expiry = new();

        public static void SetExpiry(string url, DateTime value)
            => _expiry[url] = value;

        public static bool TryGetExpiry(string url, out DateTime value)
            => _expiry.TryGetValue(url, out value);
    }
}
