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
            PrimaryJikanUrl = "";
            BackupJikanUrl = "";
            TitlePreference = TitlePreferenceType.Localized;
            OriginalTitlePreference = TitlePreferenceType.JapaneseRomaji;
            PersonLanguageFilterPreference = LanguageFilterType.All;
            MaxPeople = 0;
            MaxGenres = 5;
            EnableBestAttempt = false;
            ExcludeSpecials = false;
            UseExternalIDs = false;
            EnableNSFW = false;
            SwapVoiceActorsAndCharacters = false;
            CacheBackupOtherTime = 1;
            CacheSearchTime = 60;
            EnableDebug = false;
            ForceNewMetadata = false;

            SeriesMetadata = new SeriesMetadataConfiguration();
            SeasonMetadata = new SeasonMetadataConfiguration();
            EpisodeMetadata = new EpisodeMetadataConfiguration();
            MovieMetadata = new MovieMetadataConfiguration();
        }

        public string PrimaryJikanUrl { get; set; }
        public string BackupJikanUrl { get; set; }

        // === Title Settings ===
        public TitlePreferenceType TitlePreference { get; set; }
        public TitlePreferenceType OriginalTitlePreference { get; set; }

        // === People & Language Filters ===
        public LanguageFilterType PersonLanguageFilterPreference { get; set; }
        public int MaxPeople { get; set; }

        // === Metadata Options ===
        public int MaxGenres { get; set; }
        public bool EnableBestAttempt { get; set; }
        public bool ExcludeSpecials { get; set; }
        public bool DisableLocalCache { get; set; }
        public bool UseExternalIDs { get; set; }
        public bool EnableNSFW { get; set; }
        public bool SwapVoiceActorsAndCharacters { get; set; }

        // === Cache time ===
        public int CacheBackupOtherTime { get; set; }
        public int CacheSearchTime { get; set; }

        // === Debugging ===
        public bool EnableDebug { get; set; }
        public bool ForceNewMetadata { get; set; }

        // === Metadata Field Selection ===
        public SeriesMetadataConfiguration SeriesMetadata { get; set; }
        public SeasonMetadataConfiguration SeasonMetadata { get; set; }
        public EpisodeMetadataConfiguration EpisodeMetadata { get; set; }
        public MovieMetadataConfiguration MovieMetadata { get; set; }
    }
}
