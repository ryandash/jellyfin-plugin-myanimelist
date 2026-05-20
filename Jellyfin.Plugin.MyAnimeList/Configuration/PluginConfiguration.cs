using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MyAnimeList.Configuration
{
    /// <summary>
    /// Determines which title version should be preferred when displaying anime.
    /// </summary>
    public enum TitlePreferenceType
    {
        Localized, Japanese, JapaneseRomaji
    }

    /// <summary>
    /// Determines which language(s) to include when filtering people (voice actors, staff, etc.).
    /// </summary>
    public enum LanguageFilterType
    {
        Localized, Japanese, All
    }

    public class PluginConfiguration : BasePluginConfiguration
    {
        public PluginConfiguration()
        {
            TitlePreference = TitlePreferenceType.Localized;
            OriginalTitlePreference = TitlePreferenceType.JapaneseRomaji;
            PersonLanguageFilterPreference = LanguageFilterType.All;
            MaxPeople = 0;
            MaxGenres = 5;
            cacheBackupOtherTime = 1;
            cacheSearchTime = 60;
        }

        // === Title Settings ===
        public TitlePreferenceType TitlePreference { get; set; }
        public TitlePreferenceType OriginalTitlePreference { get; set; }

        // === People & Language Filters ===
        public LanguageFilterType PersonLanguageFilterPreference { get; set; } = LanguageFilterType.Japanese;
        public int MaxPeople { get; set; }

        // === Metadata Options ===
        public int MaxGenres { get; set; }
        public bool IgnoreMetadata { get; set; }
        public bool IgnoreEpisodeMetadata { get; set; }
        public bool EnableBestAttempt { get; set; }
        public bool ExcludeSpecials { get; set; }
        public bool DisableLocalCache { get; set; }
        public bool UseExternalIDs { get; set; }

        // === Cache time ===
        public int cacheBackupOtherTime { get; set; }
        public int cacheSearchTime { get; set; }

        // === Debugging ===
        public bool EnableDebug { get; set; }
    }
}
