using Jellyfin.Plugin.MyAnimeList.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.ExternalIds
{
    public class ExternalUrlProvider : IExternalUrlProvider
    {
        public string Name => "MyAnimeList";
        public static PluginConfiguration _config => Plugin.Instance.Configuration;

        public IEnumerable<string> GetExternalUrls(BaseItem item)
        {
            if (item.TryGetProviderId(ProviderNames.MyAnimeList, out var externalId))
            {
                switch (item)
                {
                    case Series:
                    case Movie:
                    case Season:
                        yield return $"https://myanimelist.net/anime/{externalId}/";
                        break;
                    case Person:
                    case Episode:
                        yield return externalId;
                        break;
                }
            }
        }

        public static bool TryExtractPersonId(string value, out long id)
        {
            id = 0;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (long.TryParse(value.Trim(), out id) && id > 0)
            {
                return true;
            }

            var prefix = _config.SwapVoiceActorsAndCharacters
                ? "https://myanimelist.net/character/"
                : "https://myanimelist.net/people/";

            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var remaining = value.AsSpan(prefix.Length);

            // Find the end of the numeric ID
            var length = 0;
            while (length < remaining.Length && char.IsDigit(remaining[length]))
            {
                length++;
            }

            if (length == 0)
            {
                return false;
            }

            return long.TryParse(remaining[..length], out id);
        }
    }
}
