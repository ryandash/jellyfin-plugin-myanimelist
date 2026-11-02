using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using Movie = MediaBrowser.Controller.Entities.Movies.Movie;
using Season = MediaBrowser.Controller.Entities.TV.Season;
using Series = MediaBrowser.Controller.Entities.TV.Series;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class AnimeObject : AnimeSearchResult
    {
        public List<AnimeCharacterDto> Characters { get; set; }

        public EpisodeCacheDto ToEpisodeData()
        {
            return new EpisodeCacheDto
            {
                Url = Anime.Url,
                Title = Anime.Titles.FirstOrDefault()?.Title,
                TitleJapanese = Anime.Titles.FirstOrDefault(t => t.Type == "Japanese")?.Title,
                TitleRomanji = Anime.Titles.FirstOrDefault(t => t.Type == "Default")?.Title,
                RunTimeTicks = Anime.Duration,
                Aired = Anime.Aired?.From,
                Synopsis = Anime.Synopsis
            };
        }

        public EpisodeCacheDto toEpisodeData() => ToEpisodeData();

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

            foreach (var edge in Characters ?? Enumerable.Empty<AnimeCharacterDto>())
            {
                if (edge?.Character == null || edge.VoiceActors == null)
                    continue;

                var characterUrl = edge.Character.Url;

                if (string.IsNullOrEmpty(characterUrl))
                    continue;

                PersonInfo character = null;

                foreach (var va in edge.VoiceActors)
                {
                    if (va?.Person == null || !IsAllowedLanguage(va.Language ?? string.Empty, Config.PersonLanguageFilterPreference))
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
                            Type = default,
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
                            Type = default,
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
                        var people = new List<PersonInfo>(Math.Min(limit, characters.Count + voiceActors.Count));
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
                                var voiceActorUrl = voiceActor.ProviderIds[ProviderNames.MyAnimeList];

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

        private static string[] NormalizeStringArray(IEnumerable<string> values)
        {
            return values?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
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

            if (metadata.ParentalRating)
                item.OfficialRating = Anime.Rating;

            if (metadata.RunTime)
                item.RunTimeTicks = Anime.Duration ?? 0;

            if (metadata.Genres)
                item.Genres = NormalizeStringArray(Config.MaxGenres > 0 ? Anime.Genres?.Take(Config.MaxGenres) : Anime.Genres);

            if (metadata.Studios)
                item.Studios = NormalizeStringArray(Anime.Studios);

            if (metadata.Tags)
                item.Tags = NormalizeStringArray(Anime.Tags);

            if (metadata.TrailerUrl)
                item.AddTrailerUrl(Anime.TrailerUrl);

            if (!string.IsNullOrEmpty(Anime.MalId.ToString()))
                item.SetProviderId(ProviderNames.MyAnimeList, Anime.MalId.ToString());
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
