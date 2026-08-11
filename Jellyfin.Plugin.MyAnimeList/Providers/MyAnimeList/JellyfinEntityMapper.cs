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
using Person = MediaBrowser.Controller.Entities.Person;
using Season = MediaBrowser.Controller.Entities.TV.Season;
using Series = MediaBrowser.Controller.Entities.TV.Series;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class PersonSearchResult
    {
        public static PluginConfiguration _config => Plugin.Instance.Configuration;
        public CharacterCacheDto character { get; set; }
        public PersonDto person { get; set; }

        internal Person ToPerson(PersonCreditType type)
        {
            return type switch
            {
                PersonCreditType.Characters when character != null => new Person
                {
                    Name = character.Name,
                    Overview = character.Description,
                    ProviderIds =
                    {
                        { ProviderNames.MyAnimeList, character.Url }
                    }
                },
                PersonCreditType.VoiceActors when person != null => new Person
                {
                    Name = person.Name,
                    Overview = person.Description,
                    ProviderIds =
                    {
                        { ProviderNames.MyAnimeList, person.Url }
                    }
                },
                _ => null
            };
        }
    }

    public class EpisodeSearchResult
    {
        public static PluginConfiguration _config => Plugin.Instance.Configuration;
        public EpisodeCacheDto episode { get; set; }

        public string GetPreferredTitle(TitlePreferenceType preference)
        {
            if (episode == null) return null;

            return preference switch
            {
                TitlePreferenceType.Localized => !string.IsNullOrWhiteSpace(episode.Title) ? episode.Title : episode.TitleJapanese ?? episode.TitleRomanji,
                TitlePreferenceType.Japanese => !string.IsNullOrWhiteSpace(episode.TitleJapanese) ? episode.TitleJapanese : episode.Title ?? episode.TitleRomanji,
                TitlePreferenceType.JapaneseRomaji => !string.IsNullOrWhiteSpace(episode.TitleRomanji) ? episode.TitleRomanji : episode.Title ?? episode.TitleJapanese,
                _ => episode.Title
            };
        }

        internal Episode ToEpisode(EpisodeInfo info, int totalDigits)
        {
            var aired = episode.Aired;
            var metadata = _config.EpisodeMetadata;

            Episode episodeObject = new Episode
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber,
                IndexNumberEnd = info.IndexNumberEnd
            };

            if (metadata.Name)
                episodeObject.Name = GetPreferredTitle(_config.TitlePreference);

            if (metadata.OriginalTitle)
                episodeObject.OriginalTitle = GetPreferredTitle(_config.OriginalTitlePreference);

            if (metadata.Overview)
                episodeObject.Overview = episode.Synopsis;

            if (metadata.ProductionYear)
                episodeObject.ProductionYear = aired?.Year;

            if (metadata.PremiereDate)
                episodeObject.PremiereDate = aired;

            if (metadata.EndDate)
                episodeObject.EndDate = aired;

            if (metadata.RunTime)
                episodeObject.RunTimeTicks = episode.RunTimeTicks;

            if (metadata.CommunityRating)
            {
                episodeObject.CommunityRating = episode.Score.HasValue ? (float?)(episode.Score.Value * 2) : null;
            }

            episodeObject.SetProviderId(ProviderNames.MyAnimeList, episode.Url);

            return episodeObject;
        }
    }

    public class AnimeSearchResult
    {
        public static PluginConfiguration _config => Plugin.Instance.Configuration;
        public AnimeFullCacheDto anime;

        public string GetPreferredTitle(TitlePreferenceType preference)
        {
            if (anime?.Titles == null || anime.Titles.Count == 0)
                return null;

            string preferredType = preference switch
            {
                TitlePreferenceType.Localized => "English",
                TitlePreferenceType.Japanese => "Japanese",
                TitlePreferenceType.JapaneseRomaji => "Default",
                _ => "Default"
            };

            var title = anime.Titles
                .FirstOrDefault(t =>
                    t?.Type != null &&
                    t.Type.Equals(preferredType, StringComparison.OrdinalIgnoreCase))
                ?.Title;

            if (!string.IsNullOrWhiteSpace(title))
                return title;

            // English/Japanese may not exist for every entry,
            // so always fall back to Jikan's Default/Romaji title.
            return anime.Titles
                .FirstOrDefault(t =>
                    t?.Type != null &&
                    t.Type.Equals("Default", StringComparison.OrdinalIgnoreCase))
                ?.Title
                ?? anime.Titles.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t?.Title))?.Title;
        }

        public DateTime? GetAiredDate(bool isStartDate = true) => isStartDate ? anime.Aired.From : anime.Aired.To;

        public RemoteSearchResult ToSearchResult()
        {
            var aired = GetAiredDate();
            return new RemoteSearchResult
            {
                Name = GetPreferredTitle(_config.TitlePreference),
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
                Title = GetPreferredTitle(_config.TitlePreference),
                RunTimeTicks = anime.Duration,
                Aired = anime.Aired?.From,
                Synopsis = anime.Synopsis
            };
        }

        private bool IsAllowedLanguage(string lang)
        {
            return _config.PersonLanguageFilterPreference switch
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

            var preference = _config.PersonCreditPreference;
            var maxPeople = _config.MaxPeople;

            foreach (var edge in characters ?? Enumerable.Empty<AnimeCharacterDto>())
            {
                if (edge?.Character == null || edge.VoiceActors == null)
                    continue;

                var role = edge.Character.Name;
                var characterMalUrl = edge.Character.Url;
                var characterImageUrl = edge.Character.Images?.Image;

                foreach (var va in edge.VoiceActors)
                {
                    if (va?.Person == null)
                        continue;

                    if (!IsAllowedLanguage(va.Language ?? string.Empty))
                        continue;

                    if (preference == PersonCreditType.Characters || preference == PersonCreditType.Both)
                    {
                        if (!string.IsNullOrEmpty(characterMalUrl))
                        {
                            people.Add(new PersonInfo
                            {
                                Name = role,
                                Role = va.Person.Name,
                                Type = PersonKind.Actor,
                                ImageUrl = characterImageUrl,
                                ProviderIds = new Dictionary<string, string>
                                {
                                    { ProviderNames.MyAnimeList, characterMalUrl }
                                }
                            });
                        }

                        // Only one VA is needed to establish the character credit.
                        if (preference == PersonCreditType.Characters)
                            break;
                    }

                    if (preference == PersonCreditType.VoiceActors ||  preference == PersonCreditType.Both)
                    {
                        var personMalUrl = va.Person.Url;

                        if (!string.IsNullOrEmpty(personMalUrl))
                        {
                            people.Add(new PersonInfo
                            {
                                Name = va.Person.Name,
                                Role = role,
                                Type = PersonKind.Actor,
                                ImageUrl = va.Person.Images?.Image,
                                ProviderIds = new Dictionary<string, string>
                                {
                                    { ProviderNames.MyAnimeList, personMalUrl }
                                }
                            });
                        }
                    }
                }
            }

            int limit = maxPeople > 0 ? maxPeople : int.MaxValue;

            return people.Take(limit).ToList();
        }

        public Series ToSeries(SeriesInfo info)
        {
            var aired = GetAiredDate();
            var metadata = _config.SeriesMetadata;

            Series series = new Series
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber,
            };

            if (metadata.Name)
                series.Name = GetPreferredTitle(_config.TitlePreference);

            if (metadata.OriginalTitle)
                series.OriginalTitle = GetPreferredTitle(_config.OriginalTitlePreference);

            if (metadata.Overview)
                series.Overview = anime.Synopsis;

            if (metadata.ProductionYear)
                series.ProductionYear = aired?.Year;

            if (metadata.PremiereDate)
                series.PremiereDate = aired;

            if (metadata.EndDate)
                series.EndDate = GetAiredDate(false);

            if (metadata.CommunityRating)
                series.CommunityRating = anime.Score;

            if (metadata.ParentalRating)
                series.OfficialRating = anime.Rating;

            if (metadata.RunTime)
                series.RunTimeTicks = anime.Duration;

            if (metadata.Genres)
                series.Genres = anime.Genres?
                    .Take(_config.MaxGenres)
                    .ToArray();

            if (metadata.Studios)
                series.Studios = anime.Studios;

            if (metadata.Tags)
                series.Tags = anime.Tags;

            if (metadata.Status)
            {
                series.Status = anime.Status switch
                {
                    "Finished Airing" => SeriesStatus.Ended,
                    "Currently Airing" => SeriesStatus.Continuing,
                    "Not yet aired" => SeriesStatus.Unreleased,
                    _ => SeriesStatus.Unreleased
                };
            }

            if (metadata.AirDays || metadata.AirTime)
            {
                var broadcast = anime.Broadcast;

                if (broadcast is not null)
                {
                    if (metadata.AirDays)
                        series.AirDays = broadcast.AirDays;

                    if (metadata.AirTime)
                        series.AirTime = broadcast.AirTime;
                }
            }

            series.SetProviderId(ProviderNames.MyAnimeList, anime.MalId.ToString());

            return series;
        }

        public Season ToSeason(SeasonInfo info)
        {
            var aired = GetAiredDate();
            var metadata = _config.SeasonMetadata;

            Season season = new Season
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber
            };

            if (metadata.Name)
                season.Name = GetPreferredTitle(_config.TitlePreference);

            if (metadata.OriginalTitle)
                season.OriginalTitle = GetPreferredTitle(_config.OriginalTitlePreference);

            if (metadata.Overview)
                season.Overview = anime.Synopsis;

            if (metadata.ProductionYear)
                season.ProductionYear = aired?.Year;

            if (metadata.PremiereDate)
                season.PremiereDate = aired;

            if (metadata.EndDate)
                season.EndDate = GetAiredDate(false);

            if (metadata.CommunityRating)
                season.CommunityRating = anime.Score;

            if (metadata.RunTime)
                season.RunTimeTicks = anime.Duration;

            if (metadata.Genres)
                season.Genres = anime.Genres?
                    .Take(_config.MaxGenres)
                    .ToArray();

            if (metadata.Studios)
                season.Studios = anime.Studios;

            if (metadata.Tags)
                season.Tags = anime.Tags;

            season.SetProviderId(ProviderNames.MyAnimeList, anime.MalId.ToString());

            return season;
        }

        public Movie ToMovie(MovieInfo info)
        {
            var aired = GetAiredDate();
            var metadata = _config.MovieMetadata;

            Movie movie = new Movie
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber
            };

            if (metadata.Name)
                movie.Name = GetPreferredTitle(_config.TitlePreference);

            if (metadata.OriginalTitle)
                movie.OriginalTitle = GetPreferredTitle(_config.OriginalTitlePreference);

            if (metadata.Overview)
                movie.Overview = anime.Synopsis;

            if (metadata.ProductionYear)
                movie.ProductionYear = aired?.Year;

            if (metadata.PremiereDate)
                movie.PremiereDate = aired;

            if (metadata.EndDate)
                movie.EndDate = GetAiredDate(false);

            if (metadata.CommunityRating)
                movie.CommunityRating = anime.Score;

            if (metadata.RunTime)
                movie.RunTimeTicks = anime.Duration;

            if (metadata.Genres)
                movie.Genres = anime.Genres?
                    .Take(_config.MaxGenres)
                    .ToArray();

            if (metadata.Tags)
                movie.Tags = anime.Tags;

            if (metadata.Studios)
                movie.Studios = anime.Studios;

            movie.SetProviderId(ProviderNames.MyAnimeList, anime.MalId.ToString());

            return movie;
        }
    }
}
