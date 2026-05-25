using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using JikanDotNet;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;
using System.Data;
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
                RunTimeTicks = episode.Duration.HasValue ? TimeSpan.FromSeconds(episode.Duration.Value).Ticks : null,
                CommunityRating = episode.Score > 0 ? (float?)episode.Score : null,

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

        public float GetRating() => (float)(anime.Score ?? 0.0);

        public RemoteSearchResult ToSearchResult()
        {
            return new RemoteSearchResult
            {
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                ProductionYear = GetAiredDate().HasValue ? GetAiredDate().Value.Year : null,
                PremiereDate = GetAiredDate(),
                ImageUrl = ImagesSetDto.GetImageUrl(anime.Images.JPG),
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
                Duration = GetDuration(anime.Duration),
                Aired = anime.Aired?.From,
                Synopsis = anime.Synopsis
            };
        }

        private int GetDuration(string duration)
        {
            var parts = duration.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var totalMinutes = 0;

            for (var i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out var value))
                {
                    if (i + 1 < parts.Length)
                    {
                        totalMinutes += parts[i + 1] switch
                        {
                            var s when s.StartsWith("hr", StringComparison.OrdinalIgnoreCase) => value * 60,
                            var s when s.StartsWith("min", StringComparison.OrdinalIgnoreCase) => value,
                            _ => 0
                        };

                        if (parts[i + 1].StartsWith("hr", StringComparison.OrdinalIgnoreCase) ||
                            parts[i + 1].StartsWith("min", StringComparison.OrdinalIgnoreCase))
                        {
                            i++; // Skip next part as it's already processed
                        }
                    }
                }
            }

            return totalMinutes;
        }
        private static string SwapName(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            var parts = input.Split(',');
            return parts.Length == 2
                ? $"{parts[1].Trim()} {parts[0].Trim()}"
                : input.Trim();
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

        private static string Normalize(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim().ToLowerInvariant();
        }

        public List<PersonInfo> GetPeopleInfo()
        {
            var people = new List<PersonInfo>();

            foreach (var edge in characters ?? Enumerable.Empty<AnimeCharacterDto>())
            {
                if (edge.VoiceActors == null)
                    continue;

                foreach (var va in edge.VoiceActors)
                {
                    if (!IsAllowedLanguage(va.Language ?? string.Empty))
                        continue;

                    var name = SwapName(va.Person.Name);
                    var role = SwapName(edge.Character.Name);

                    var key = (Normalize(name), Normalize(role));

                    var newPerson = new PersonInfo
                    {
                        Name = name,
                        Role = role,
                        Type = PersonKind.Actor,
                        ImageUrl = ImagesSetDto.GetImageUrl(va.Person.Images.JPG),
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

        public string[] GetGenres()
        {
            var malGenres = anime?.Genres?
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                ?? Enumerable.Empty<string>();

            if (config.MaxGenres > 0)
                malGenres = malGenres.Take(config.MaxGenres);

            return malGenres.ToArray();
        }

        public string[] GetStudioNames()
        {
            var malStudios = anime?.Studios?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                ?? Enumerable.Empty<string>();

            return malStudios.ToArray();
        }

        public Series ToSeries(SeriesInfo info)
        {
            var aired = GetAiredDate();
            var duration = GetDuration(anime.Duration);
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
                CommunityRating = GetRating() > 0 ? (float?)GetRating() : null,
                RunTimeTicks = duration > 0 ? TimeSpan.FromMinutes(duration).Ticks : null,
                Genres = GetGenres(),
                Studios = GetStudioNames(),
                Status = anime.Status switch
                {
                    "Finished Airing" => SeriesStatus.Ended,
                    "Currently Airing" => SeriesStatus.Continuing,
                    "Not yet aired" => SeriesStatus.Unreleased,
                    _ => SeriesStatus.Unreleased
                }
            };

            series.SetProviderId(ProviderNames.MyAnimeList, anime.MalId.ToString());

            return series;
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
                CommunityRating = GetRating() > 0 ? (float?)GetRating() : null,
                Genres = GetGenres(),
                Studios = GetStudioNames(),
            };

            movie.SetProviderId(ProviderNames.MyAnimeList, anime.MalId.ToString());

            return movie;
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
                CommunityRating = GetRating() > 0 ? (float?)GetRating() : null,
                Genres = GetGenres(),
                Studios = GetStudioNames(),
            };

            season.SetProviderId(ProviderNames.MyAnimeList, anime.MalId.ToString());

            return season;
        }
    }
}
