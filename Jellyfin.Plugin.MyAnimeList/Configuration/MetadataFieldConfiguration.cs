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
    /// Metadata configuration specific to Episode.
    /// </summary>
    public class EpisodeMetadataConfiguration : BaseMetadataConfiguration
    {
    }

    public class CommonMetadataConfiguration : BaseMetadataConfiguration
    {
        public bool Genres { get; set; } = true;
        public bool Studios { get; set; } = true;
        public bool People { get; set; } = true;
        public bool Tags { get; set; } = true;
        public bool TrailerUrl { get; set; } = true;
        public bool ParentalRating { get; set; } = true;
    }

    /// <summary>
    /// Metadata configuration specific to Series.
    /// </summary>
    public class SeriesMetadataConfiguration : CommonMetadataConfiguration
    {
        public bool Status { get; set; } = true;
        public bool AirDays { get; set; } = true;
        public bool AirTime { get; set; } = true;
    }

    /// <summary>
    /// Metadata configuration specific to Season.
    /// </summary>
    public class SeasonMetadataConfiguration : CommonMetadataConfiguration
    {

    }



    /// <summary>
    /// Metadata configuration specific to Movie.
    /// </summary>
    public class MovieMetadataConfiguration : CommonMetadataConfiguration
    {
    }
}
