using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using Episode = MediaBrowser.Controller.Entities.TV.Episode;
using Movie = MediaBrowser.Controller.Entities.Movies.Movie;
using Season = MediaBrowser.Controller.Entities.TV.Season;
using Series = MediaBrowser.Controller.Entities.TV.Series;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class EpisodeSearchResult
    {
        public static PluginConfiguration config = Plugin.Instance.Configuration;
        public EpisodeCacheDto episode { get; set; }

        public string GetPreferredTitle(TitlePreferenceType preference, string language)
        {
            return preference switch
            {
                TitlePreferenceType.Localized => language switch
                {
                    "en" => episode.Title,
                    "jap" => episode.TitleJapanese,
                    "romaji" => episode.TitleRomanji,
                    _ => episode.Title
                },
                TitlePreferenceType.Japanese => episode.TitleJapanese ?? episode.Title,
                TitlePreferenceType.JapaneseRomaji => episode.TitleRomanji ?? episode.Title,
                _ => episode.Title
            };
        }

        internal Episode ToEpisode(EpisodeInfo info, int totalDigits)
        {
            var aired = episode.Aired;
            Episode episodeObject = new Episode
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber,
                IndexNumberEnd = info.IndexNumberEnd,
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                OriginalTitle = GetPreferredTitle(config.OriginalTitlePreference, "romaji"),
                Overview = episode.Synopsis,
                ProductionYear = aired?.Year,
                PremiereDate = aired,
                EndDate = aired,
                RunTimeTicks = episode.RunTimeTicks,
                CommunityRating = episode.Score.HasValue ? (float?)(episode.Score.Value * 2) : null
            };

            episodeObject.SetProviderId(ProviderNames.MyAnimeList, episode.Url);
            return episodeObject;
        }
    }

    public class AnimeSearchResult
    {
        public static PluginConfiguration config = Plugin.Instance.Configuration;
        public AnimeFullCacheDto anime;

        public string GetPreferredTitle(TitlePreferenceType preference, string language)
        {
            var titleType = preference switch
            {
                TitlePreferenceType.Localized => language switch
                {
                    "en" => "English",
                    "jap" => "Japanese",
                    _ => "Default"
                },
                TitlePreferenceType.Japanese => "Japanese",
                _ => "Default"
            };

            return anime.Titles
                .FirstOrDefault(t => t.Type.Equals(titleType, StringComparison.OrdinalIgnoreCase))?.Title
                ?? anime.Titles.FirstOrDefault(t => t.Type.Equals("Default", StringComparison.OrdinalIgnoreCase))?.Title;
        }

        public DateTime? GetAiredDate(bool isStartDate = true) => isStartDate ? anime.Aired.From : anime.Aired.To;

        public RemoteSearchResult ToSearchResult()
        {
            var aired = GetAiredDate();
            return new RemoteSearchResult
            {
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                ProductionYear = aired.HasValue ? aired.Value.Year : null,
                PremiereDate = aired,
                ImageUrl = anime.Images.Image,
                SearchProviderName = ProviderNames.MyAnimeList,
                ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, anime.MalId.ToString() } }
            };
        }
    }

    public class AnimeObject : AnimeSearchResult
    {
        public List<AnimeCharacterDto> characters { get; set; }

        public EpisodeCacheDto toEpisodeData()
        {
            return new EpisodeCacheDto
            {
                Url = anime.Url,
                Title = GetPreferredTitle(config.TitlePreference, "en"),
                RunTimeTicks = anime.Duration,
                Aired = anime.Aired?.From,
                Synopsis = anime.Synopsis
            };
        }

        private bool IsAllowedLanguage(string lang)
        {
            return config.PersonLanguageFilterPreference switch
            {
                LanguageFilterType.All => true,
                LanguageFilterType.Japanese => lang == "Japanese",
                LanguageFilterType.Localized => lang != "Japanese",
                _ => true
            };
        }

        public List<PersonInfo> GetPeopleInfo()
        {
            var people = new List<PersonInfo>();

            foreach (var edge in characters ?? Enumerable.Empty<AnimeCharacterDto>())
            {
                if (edge.VoiceActors == null)
                    continue;

                var role = edge.Character.Name;

                foreach (var va in edge.VoiceActors)
                {
                    if (!IsAllowedLanguage(va.Language ?? string.Empty))
                        continue;

                    var newPerson = new PersonInfo
                    {
                        Name = va.Person.Name,
                        Role = role,
                        Type = PersonKind.Actor,
                        ImageUrl = va.Person.Images.Image,
                        ProviderIds = new Dictionary<string, string>
                        {
                            { ProviderNames.MyAnimeList, va.Person.MalId.ToString() }
                        }
                    };

                    people.Add(newPerson);
                }
            }

            int limit = config.MaxPeople > 0 ? config.MaxPeople : int.MaxValue;

            return people.Take(limit).ToList();
        }

        public Series ToSeries(SeriesInfo info)
        {
            var aired = GetAiredDate();
            Series series = new Series
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber,
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                OriginalTitle = GetPreferredTitle(config.OriginalTitlePreference, "romaji"),
                Overview = anime.Synopsis,
                ProductionYear = aired?.Year,
                PremiereDate = aired,
                EndDate = GetAiredDate(false),
                CommunityRating = anime.Score,
                RunTimeTicks = anime.Duration,
                Genres = anime.Genres.Take(config.MaxGenres).ToArray(),
                Studios = anime.Studios,
                Status = anime.Status switch
                {
                    "Finished Airing" => SeriesStatus.Ended,
                    "Currently Airing" => SeriesStatus.Continuing,
                    "Not yet aired" => SeriesStatus.Unreleased,
                    _ => SeriesStatus.Unreleased
                }
            };

            var broadcast = anime.Broadcast;

            if (broadcast is not null)
            {
                series.AirDays = broadcast.AirDays;
                series.AirTime = broadcast.AirTime;
            }

            series.SetProviderId(ProviderNames.MyAnimeList, anime.MalId.ToString());

            return series;
        }

        public Season ToSeason(SeasonInfo info)
        {
            var aired = GetAiredDate();
            Season season = new Season
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber,
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                OriginalTitle = GetPreferredTitle(config.OriginalTitlePreference, "romaji"),
                Overview = anime.Synopsis,
                ProductionYear = aired?.Year,
                PremiereDate = aired,
                EndDate = GetAiredDate(false),
                CommunityRating = anime.Score,
                RunTimeTicks = anime.Duration,
                Genres = anime.Genres.Take(config.MaxGenres).ToArray(),
                Studios = anime.Studios,
            };

            season.SetProviderId(ProviderNames.MyAnimeList, anime.MalId.ToString());

            return season;
        }

        public Movie ToMovie(MovieInfo info)
        {
            var aired = GetAiredDate();
            Movie movie = new Movie
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber,
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                OriginalTitle = GetPreferredTitle(config.OriginalTitlePreference, "romaji"),
                Overview = anime.Synopsis,
                ProductionYear = aired?.Year,
                PremiereDate = aired,
                EndDate = GetAiredDate(false),
                CommunityRating = anime.Score,
                RunTimeTicks = anime.Duration,
                Genres = anime.Genres.Take(config.MaxGenres).ToArray(),
                Studios = anime.Studios,
            };

            movie.SetProviderId(ProviderNames.MyAnimeList, anime.MalId.ToString());

            return movie;
        }
    }
}
