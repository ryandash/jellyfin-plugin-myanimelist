using Jellyfin.Plugin.MyAnimeList.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public abstract class JellyfinEntityMapperBase
    {
        protected static PluginConfiguration Config => Plugin.Instance.Configuration;

        protected sealed class MetadataData
        {
            public Func<TitlePreferenceType, string> TitleSelector { get; init; }
            public Func<TitlePreferenceType, string> OriginalTitleSelector { get; init; }
            public string Overview { get; init; }
            public DateTime? PremiereDate { get; init; }
            public DateTime? EndDate { get; init; }
            public float? CommunityRating { get; init; }
            public string OfficialRating { get; init; }
            public long? RunTimeTicks { get; init; }
            public IEnumerable<string> Genres { get; init; }
            public string[] Studios { get; init; }
            public IEnumerable<string> Tags { get; init; }
            public string TrailerUrl { get; init; }
            public string ProviderId { get; init; }
        }

        protected static string GetPreferredTitle<TTitle>(
            IEnumerable<TTitle> titles,
            TitlePreferenceType preference,
            Func<TTitle, string> typeSelector,
            Func<TTitle, string> titleSelector)
        {
            if (titles == null)
                return null;

            var titleList = titles
                .Where(title => title != null)
                .ToList();

            if (titleList.Count == 0)
                return null;

            var preferredType = preference switch
            {
                TitlePreferenceType.Localized => "English",
                TitlePreferenceType.Japanese => "Japanese",
                TitlePreferenceType.JapaneseRomaji => "Default",
                _ => "Default"
            };

            var preferred = titleList.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(typeSelector(item)) &&
                typeSelector(item).Equals(preferredType, StringComparison.OrdinalIgnoreCase));

            if (preferred != null)
                return titleSelector(preferred);

            var defaultTitle = titleList.FirstOrDefault(item =>
                !string.IsNullOrWhiteSpace(typeSelector(item)) &&
                typeSelector(item).Equals("Default", StringComparison.OrdinalIgnoreCase));

            if (defaultTitle != null)
                return titleSelector(defaultTitle);

            return titleList
                .Select(titleSelector)
                .FirstOrDefault(title => !string.IsNullOrWhiteSpace(title));
        }

        protected static bool IsAllowedLanguage(string lang, LanguageFilterType filter)
        {
            return filter switch
            {
                LanguageFilterType.All => true,
                LanguageFilterType.Japanese => lang == "Japanese",
                LanguageFilterType.Localized => lang != "Japanese",
                _ => true
            };
        }
    }
}
