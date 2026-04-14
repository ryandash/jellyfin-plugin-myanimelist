using System;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem
{
    public interface ICacheStore
    {
        T Get<T>(string key);
        void Put<T>(string key, T value, DateTime expiry);
    }
}
