namespace Jellyfin.Plugin.MyAnimeList.Configuration
{
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

    public class SeriesMetadataConfiguration : CommonMetadataConfiguration
    {
        public bool Status { get; set; } = true;
        public bool AirDays { get; set; } = true;
        public bool AirTime { get; set; } = true;
    }

    public class SeasonMetadataConfiguration : CommonMetadataConfiguration
    {

    }

    public class MovieMetadataConfiguration : CommonMetadataConfiguration
    {
    }

    public class BookMetadataConfiguration
    {
        public bool SeriesName { get; set; } = true;
        public bool Name { get; set; } = true;
        public bool Chapters { get; set; } = true;
        public bool Volumes { get; set; } = true;
        public bool Overview { get; set; } = true;
        public bool ProductionYear { get; set; } = true;
        public bool CommunityRating { get; set; } = true;
        public bool Genres { get; set; } = true;
        public bool Studios { get; set; } = true;
        public bool People { get; set; } = true;
        public bool Tags { get; set; } = true;
    }
}
