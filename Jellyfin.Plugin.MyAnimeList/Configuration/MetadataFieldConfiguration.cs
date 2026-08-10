namespace Jellyfin.Plugin.MyAnimeList.Configuration
{
    /// <summary>
    /// Contains metadata fields common to Series, Season, Episode, and Movie.
    /// </summary>
    public class BaseMetadataConfiguration
    {
        public bool Name { get; set; } = true;
        public bool OriginalTitle { get; set; } = true;
        public bool Overview { get; set; } = true;
        public bool ProductionYear { get; set; } = true;
        public bool PremiereDate { get; set; } = true;
        public bool EndDate { get; set; } = true;
        public bool CommunityRating { get; set; } = true;
        public bool RunTime { get; set; } = true;
    }

    /// <summary>
    /// Metadata configuration specific to Series.
    /// </summary>
    public class SeriesMetadataConfiguration : BaseMetadataConfiguration
    {
        public bool Genres { get; set; } = true;
        public bool Studios { get; set; } = true;
        public bool Status { get; set; } = true;
        public bool AirDays { get; set; } = true;
        public bool AirTime { get; set; } = true;
        public bool People { get; set; } = true;
    }

    /// <summary>
    /// Metadata configuration specific to Season.
    /// </summary>
    public class SeasonMetadataConfiguration : BaseMetadataConfiguration
    {
        public bool Genres { get; set; } = true;
        public bool Studios { get; set; } = true;
        public bool People { get; set; } = true;
    }

    /// <summary>
    /// Metadata configuration specific to Episode.
    /// </summary>
    public class EpisodeMetadataConfiguration : BaseMetadataConfiguration
    {
    }

    /// <summary>
    /// Metadata configuration specific to Movie.
    /// </summary>
    public class MovieMetadataConfiguration : BaseMetadataConfiguration
    {
        public bool Genres { get; set; } = true;
        public bool Studios { get; set; } = true;
        public bool People { get; set; } = true;
    }
}
