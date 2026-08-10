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

        public static bool TryExtractPersonId(string value, out long id, out PersonCreditType type)
        {
            id = 0;
            type = PersonCreditType.VoiceActors;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            value = value.Trim();

            // Provider IDs may already be stored as a numeric MAL ID.
            // There is no way to determine whether a numeric-only ID is
            // a person or character, so use the configured preference.
            if (long.TryParse(value, out id) && id > 0)
            {
                type = _config.PersonCreditPreference switch
                {
                    PersonCreditType.Characters => PersonCreditType.Characters,
                    _ => PersonCreditType.VoiceActors
                };

                return true;
            }

            const string peoplePrefix = "https://myanimelist.net/people/";
            const string characterPrefix = "https://myanimelist.net/character/";

            string prefix;

            if (value.StartsWith(peoplePrefix, StringComparison.OrdinalIgnoreCase))
            {
                prefix = peoplePrefix;
                type = PersonCreditType.VoiceActors;
            }
            else if (value.StartsWith(characterPrefix, StringComparison.OrdinalIgnoreCase))
            {
                prefix = characterPrefix;
                type = PersonCreditType.Characters;
            }
            else
            {
                return false;
            }

            var remaining = value.AsSpan(prefix.Length);

            var length = 0;

            while (length < remaining.Length && char.IsDigit(remaining[length]))
            {
                length++;
            }

            if (length == 0)
            {
                return false;
            }

            return long.TryParse(remaining[..length], out id) && id > 0;
        }
    }
}
