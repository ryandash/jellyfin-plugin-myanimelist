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
        public CharacterCacheDto Character { get; set; }
        public PersonDto Person { get; set; }

        internal Person ToPerson(PersonCreditType type)
        {
            return type switch
            {
                PersonCreditType.Characters when Character != null => new Person
                {
                    Name = Character.Name,
                    Overview = Character.Description,
                    ProviderIds =
                    {
                        { ProviderNames.MyAnimeList, Character.Url }
                    }
                },
                PersonCreditType.VoiceActors when Person != null => new Person
                {
                    Name = Person.Name,
                    Overview = Person.Description,
                    ProviderIds =
                    {
                        { ProviderNames.MyAnimeList, Person.Url }
                    }
                },
                _ => null
            };
        }
    }

    public class EpisodeSearchResult
    {
        public static PluginConfiguration Config => Plugin.Instance.Configuration;
        public EpisodeCacheDto Episode { get; set; }

        public string GetPreferredTitle(TitlePreferenceType preference)
        {
            if (Episode == null) return null;

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

            Episode episodeObject = new Episode
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
            {
                episodeObject.CommunityRating = Episode.Score.HasValue ? (float?)(Episode.Score.Value * 2) : null;
            }

            episodeObject.SetProviderId(ProviderNames.MyAnimeList, Episode.Url);

            return episodeObject;
        }
    }

    public class AnimeSearchResult
    {
        public static PluginConfiguration Config => Plugin.Instance.Configuration;
        public AnimeFullCacheDto Anime { get; set; }

        public string GetPreferredTitle(TitlePreferenceType preference)
        {
            if (Anime?.Titles == null || Anime.Titles.Count == 0)
                return null;

            string preferredType = preference switch
            {
                TitlePreferenceType.Localized => "English",
                TitlePreferenceType.Japanese => "Japanese",
                TitlePreferenceType.JapaneseRomaji => "Default",
                _ => "Default"
            };

            var title = Anime.Titles
                .FirstOrDefault(t =>
                    t?.Type != null &&
                    t.Type.Equals(preferredType, StringComparison.OrdinalIgnoreCase))
                ?.Title;

            if (!string.IsNullOrWhiteSpace(title))
                return title;

            // English/Japanese may not exist for every entry,
            // so always fall back to Jikan's Default/Romaji title.
            return Anime.Titles
                .FirstOrDefault(t =>
                    t?.Type != null &&
                    t.Type.Equals("Default", StringComparison.OrdinalIgnoreCase))
                ?.Title
                ?? Anime.Titles.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t?.Title))?.Title;
        }

        public DateTime? GetAiredDate(bool isStartDate = true) => isStartDate ? Anime.Aired.From : Anime.Aired.To;

        public RemoteSearchResult ToSearchResult()
        {
            var aired = GetAiredDate();
            return new RemoteSearchResult
            {
                Name = GetPreferredTitle(Config.TitlePreference),
                ProductionYear = aired.HasValue ? aired.Value.Year : null,
                PremiereDate = aired,
                ImageUrl = Anime.Images.Image,
                SearchProviderName = ProviderNames.MyAnimeList,
                ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, Anime.MalId.ToString() } }
            };
        }
    }

    public class AnimeObject : AnimeSearchResult
    {
        public List<AnimeCharacterDto> Characters { get; set; }

        public EpisodeCacheDto toEpisodeData()
        {
            return new EpisodeCacheDto
            {
                Url = Anime.Url,
                Title = GetPreferredTitle(Config.TitlePreference),
                RunTimeTicks = Anime.Duration,
                Aired = Anime.Aired?.From,
                Synopsis = Anime.Synopsis
            };
        }

        private bool IsAllowedLanguage(string lang)
        {
            return Config.PersonLanguageFilterPreference switch
            {
                LanguageFilterType.All => true,
                LanguageFilterType.Japanese => lang == "Japanese",
                LanguageFilterType.Localized => lang != "Japanese",
                _ => true
            };
        }

        public List<PersonInfo> GetPeopleInfo()
        {
            var preference = Config.PersonCreditPreference;
            var maxPeople = Config.MaxPeople;
            var limit = maxPeople > 0 ? maxPeople : int.MaxValue;

            var includeCharacters = preference == PersonCreditType.Characters || preference == PersonCreditType.Both;
            var includeVoiceActors = preference == PersonCreditType.VoiceActors || preference == PersonCreditType.Both;

            var characters = new List<PersonInfo>();
            var charactersByVoiceActor = new Dictionary<string, List<PersonInfo>>();
            var voiceActors = new Dictionary<string, PersonInfo>();

            foreach (var edge in this.Characters ?? Enumerable.Empty<AnimeCharacterDto>())
            {
                if (edge?.Character == null || edge.VoiceActors == null)
                    continue;

                var characterUrl = edge.Character.Url;

                if (string.IsNullOrEmpty(characterUrl))
                    continue;

                PersonInfo character = null;

                foreach (var va in edge.VoiceActors)
                {
                    if (va?.Person == null || !IsAllowedLanguage(va.Language ?? string.Empty))
                        continue;

                    var personUrl = va.Person.Url;

                    if (string.IsNullOrEmpty(personUrl))
                        continue;

                    if (includeCharacters)
                    {
                        character ??= new PersonInfo
                        {
                            Name = edge.Character.Name,
                            Role = va.Person.Name,
                            Type = PersonKind.Actor,
                            ImageUrl = edge.Character.Images?.Image,
                            ProviderIds = new Dictionary<string, string>
                            {
                                { ProviderNames.MyAnimeList, characterUrl }
                            }
                        };

                        if (characters.Count == 0 || !ReferenceEquals(characters[^1], character))
                        {
                            characters.Add(character);
                        }

                        if (!charactersByVoiceActor.TryGetValue(personUrl, out var characterList))
                        {
                            characterList = new List<PersonInfo>();
                            charactersByVoiceActor[personUrl] = characterList;
                        }

                        characterList.Add(character);
                    }

                    if (includeVoiceActors && !voiceActors.ContainsKey(personUrl))
                    {
                        voiceActors[personUrl] = new PersonInfo
                        {
                            Name = va.Person.Name,
                            Role = edge.Character.Name,
                            Type = PersonKind.Actor,
                            ImageUrl = va.Person.Images?.Image,
                            ProviderIds = new Dictionary<string, string>
                            {
                                { ProviderNames.MyAnimeList, personUrl }
                            }
                        };
                    }
                }
            }

            switch (preference)
            {
                case PersonCreditType.Characters:
                    return characters.Take(limit).ToList();

                case PersonCreditType.VoiceActors:
                    return voiceActors.Values.Take(limit).ToList();

                case PersonCreditType.Both:
                    {
                        var people = new List<PersonInfo>(
                            Math.Min(limit, characters.Count + voiceActors.Count));

                        var addedCharacters = new HashSet<string>();
                        var addedVoiceActors = new HashSet<string>();

                        foreach (var group in charactersByVoiceActor)
                        {
                            foreach (var character in group.Value)
                            {
                                var characterUrl = character.ProviderIds[ProviderNames.MyAnimeList];

                                if (!addedCharacters.Add(characterUrl))
                                    continue;

                                people.Add(character);
                            }

                            if (voiceActors.TryGetValue(group.Key, out var voiceActor))
                            {
                                var voiceActorUrl =
                                    voiceActor.ProviderIds[ProviderNames.MyAnimeList];

                                if (addedVoiceActors.Add(voiceActorUrl))
                                {
                                    people.Add(voiceActor);
                                }
                            }

                            if (people.Count >= limit)
                                return people;
                        }

                        return people;
                    }

                default:
                    return [];
            }
        }

        public interface IMetadataOptions
        {
            bool Name { get; }
            bool OriginalTitle { get; }
            bool Overview { get; }
            bool ProductionYear { get; }
            bool PremiereDate { get; }
            bool EndDate { get; }
            bool CommunityRating { get; }
            bool RunTime { get; }
            bool Genres { get; }
            bool Studios { get; }
            bool Tags { get; }
            bool TrailerUrl { get; }
        }

        private void ApplyCommonMetadata(BaseItem item, CommonMetadataConfiguration metadata)
        {
            var aired = GetAiredDate();

            if (metadata.Name)
                item.Name = GetPreferredTitle(Config.TitlePreference);

            if (metadata.OriginalTitle)
                item.OriginalTitle = GetPreferredTitle(Config.OriginalTitlePreference);

            if (metadata.Overview)
                item.Overview = Anime.Synopsis;

            if (metadata.ProductionYear)
                item.ProductionYear = aired?.Year;

            if (metadata.PremiereDate)
                item.PremiereDate = aired;

            if (metadata.EndDate)
                item.EndDate = GetAiredDate(false);

            if (metadata.CommunityRating)
                item.CommunityRating = Anime.Score;

            if (metadata.RunTime)
                item.RunTimeTicks = Anime.Duration;

            if (metadata.Genres)
                item.Genres = Anime.Genres?
                    .Take(Config.MaxGenres)
                    .ToArray();

            if (metadata.Studios)
                item.Studios = Anime.Studios;

            if (metadata.Tags)
                item.Tags = Anime.Tags;

            if (metadata.TrailerUrl && !string.IsNullOrEmpty(Anime.TrailerUrl))
                item.AddTrailerUrl(Anime.TrailerUrl);

            item.SetProviderId(
                ProviderNames.MyAnimeList,
                Anime.MalId.ToString());
        }

        public Series ToSeries(SeriesInfo info)
        {
            var metadata = Config.SeriesMetadata;
            
            var series = new Series
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber
            };

            ApplyCommonMetadata(series, metadata);

            if (metadata.ParentalRating)
                series.OfficialRating = Anime.Rating;

            if (metadata.Status)
            {
                series.Status = Anime.Status switch
                {
                    "Finished Airing" => SeriesStatus.Ended,
                    "Currently Airing" => SeriesStatus.Continuing,
                    _ => SeriesStatus.Unreleased
                };
            }

            if (metadata.AirDays || metadata.AirTime)
            {
                var broadcast = Anime.Broadcast;

                if (broadcast != null)
                {
                    if (metadata.AirDays)
                        series.AirDays = broadcast.AirDays;

                    if (metadata.AirTime)
                        series.AirTime = broadcast.AirTime;
                }
            }

            return series;
        }

        public Season ToSeason(SeasonInfo info)
        {
            var season = new Season
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber
            };

            ApplyCommonMetadata(season, Config.SeasonMetadata);

            return season;
        }

        public Movie ToMovie(MovieInfo info)
        {
            var movie = new Movie
            {
                IndexNumber = info.IndexNumber,
                ParentIndexNumber = info.ParentIndexNumber
            };

            ApplyCommonMetadata(movie, Config.MovieMetadata);

            return movie;
        }
    }
}
