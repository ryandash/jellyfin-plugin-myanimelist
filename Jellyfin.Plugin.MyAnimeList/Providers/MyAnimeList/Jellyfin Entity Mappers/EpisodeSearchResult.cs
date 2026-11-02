using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class EpisodeSearchResult : JellyfinEntityMapperBase
    {
        public EpisodeCacheDto Episode { get; set; }

        public string GetPreferredTitle(TitlePreferenceType preference)
        {
            if (Episode == null)
                return null;

            return preference switch
            {
                TitlePreferenceType.Localized => !string.IsNullOrWhiteSpace(Episode.Title) ? Episode.Title : Episode.TitleJapanese ?? Episode.TitleRomanji,
                TitlePreferenceType.Japanese => !string.IsNullOrWhiteSpace(Episode.TitleJapanese) ? Episode.TitleJapanese : Episode.Title ?? Episode.TitleRomanji,
                TitlePreferenceType.JapaneseRomaji => !string.IsNullOrWhiteSpace(Episode.TitleRomanji) ? Episode.TitleRomanji : Episode.Title ?? Episode.TitleJapanese,
                _ => Episode.Title
            };
        }

        internal Episode ToEpisode(EpisodeInfo info)
        {
            var metadata = Config.EpisodeMetadata;
            var aired = Episode.Aired;

            var episodeObject = new Episode
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber,
                IndexNumberEnd = info.IndexNumberEnd
            };

            if (metadata.Name)
                episodeObject.Name = GetPreferredTitle(Config.TitlePreference);

            if (metadata.OriginalTitle)
                episodeObject.OriginalTitle = GetPreferredTitle(Config.OriginalTitlePreference);

            if (metadata.Overview)
                episodeObject.Overview = Episode.Synopsis;

            if (metadata.ProductionYear)
                episodeObject.ProductionYear = aired?.Year;

            if (metadata.PremiereDate)
                episodeObject.PremiereDate = aired;

            if (metadata.EndDate)
                episodeObject.EndDate = aired;

            if (metadata.RunTime)
                episodeObject.RunTimeTicks = Episode.RunTimeTicks;

            if (metadata.CommunityRating)
                episodeObject.CommunityRating = Episode.Score.HasValue ? (float?)(Episode.Score.Value * 2) : null;

            episodeObject.SetProviderId(ProviderNames.MyAnimeList, Episode.Url);

            return episodeObject;
        }
    }
}
