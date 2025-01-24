using System;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs
{
    using JikanDotNet;

    public static class JikanSingleton
    {
        private static readonly Lazy<Jikan> _jikanInstance = new Lazy<Jikan>(() => new Jikan());

        public static Jikan Instance => _jikanInstance.Value;
    }
}
