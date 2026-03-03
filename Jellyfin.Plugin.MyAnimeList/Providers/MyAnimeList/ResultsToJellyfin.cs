using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class EpisodeSearchResult
    {
        public EpisodeCacheDto episode { get; set; }

        public DateTime? GetDate() => episode.Aired;

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

        internal Episode ToEpisode(int totalDigits)
        {
            var config = Plugin.Instance.Configuration;

            return new Episode
            {
                ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, episode.Url } },
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                OriginalTitle = GetPreferredTitle(config.OriginalTitlePreference, "romaji"),
                ProductionYear = GetDate()?.Year,
                EndDate = GetDate(),
                RunTimeTicks = episode.Duration.HasValue ? TimeSpan.FromSeconds(episode.Duration.Value).Ticks : null,
                Overview = episode.Synopsis
            };
        }
    }

    public class AnimeSearchResult
    {
        public AnimeCacheDto anime;

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

        public string GetImageUrl()
        {
            var jpg = anime.Images.JPG;
            return jpg.MaximumImageUrl ?? jpg.LargeImageUrl ?? jpg.MediumImageUrl ?? jpg.ImageUrl ?? jpg.SmallImageUrl;
        }

        public DateTime? GetAiredDate(bool isStartDate = true) => isStartDate ? anime.Aired.From : anime.Aired.To;

        public float GetRating() => (float)(anime.Score ?? 0.0);

        public RemoteSearchResult ToSearchResult()
        {
            var config = Plugin.Instance.Configuration;
            return new RemoteSearchResult
            {
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                ProductionYear = GetAiredDate().HasValue ? GetAiredDate().Value.Year : null,
                PremiereDate = GetAiredDate(),
                ImageUrl = GetImageUrl(),
                SearchProviderName = ProviderNames.MyAnimeList,
                ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, anime.MalId.ToString() } }
            };
        }
    }

    public class Anime : AnimeSearchResult
    {
        public List<CharacterCacheDto> characters { get; set; }

        public EpisodeCacheDto toEpisodeData()
        {
            var config = Plugin.Instance.Configuration;
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

        public List<PersonInfo> GetPeopleInfo()
        {
            var config = Plugin.Instance.Configuration;

            return characters
                .SelectMany(edge => edge.VoiceActors, (edge, va) => new { edge, va })
                .Where(x =>
                {
                    if (config.PersonLanguageFilterPreference == LanguageFilterType.All) return true;

                    return config.PersonLanguageFilterPreference switch
                    {
                        LanguageFilterType.Japanese => x.va.Language == "Japanese",
                        LanguageFilterType.Localized => x.va.Language != "Japanese",
                        _ => true
                    };
                })
                .Select(x => new PersonInfo
                {
                    Name = SwapName(x.va.Person.Name),
                    Role = SwapName(x.edge.Character.Name),
                    Type = PersonKind.Actor,
                    ImageUrl = x.va.Person.Images.JPG.MaximumImageUrl
                              ?? x.va.Person.Images.JPG.LargeImageUrl
                              ?? x.va.Person.Images.JPG.ImageUrl
                              ?? x.va.Person.Images.JPG.MediumImageUrl
                              ?? x.va.Person.Images.JPG.SmallImageUrl,
                    ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, x.va.Person.Url } }
                })
                .Take(config.MaxPeople > 0 ? config.MaxPeople : int.MaxValue)
                .ToList(); ;
        }

        public string[] GetGenres()
        {
            var genres = anime?.Genres?
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .ToArray() ?? Array.Empty<string>();

            var max = Plugin.Instance.Configuration.MaxGenres;
            return max > 0 ? genres.Take(max).ToArray() : genres;
        }

        public string[] GetStudioNames() =>
            anime?.Studios?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray()
            ?? Array.Empty<string>();

        public Series ToSeries()
        {
            var config = Plugin.Instance.Configuration;
            var duration = GetDuration(anime.Duration);
            return new Series
            {
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                OriginalTitle = GetPreferredTitle(config.OriginalTitlePreference, "romaji"),
                Overview = anime.Synopsis,
                ProductionYear = GetAiredDate().HasValue ? GetAiredDate().Value.Year : null,
                PremiereDate = GetAiredDate(),
                EndDate = GetAiredDate(),
                CommunityRating = GetRating(),
                RunTimeTicks = duration > 0 ? TimeSpan.FromMinutes(duration).Ticks : null,
                Genres = GetGenres(),
                Studios = GetStudioNames(),
                ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, anime.MalId.ToString() } },
                Status = anime.Status switch
                {
                    "Finished Airing" => SeriesStatus.Ended,
                    "Currently Airing" => SeriesStatus.Continuing,
                    "Not yet aired" => SeriesStatus.Unreleased,
                    _ => SeriesStatus.Unreleased
                }
            }; ;
        }

        public Movie ToMovie()
        {
            var config = Plugin.Instance.Configuration;
            return new Movie
            {
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                OriginalTitle = GetPreferredTitle(config.OriginalTitlePreference, "romaji"),
                Overview = anime.Synopsis,
                ProductionYear = GetAiredDate().HasValue ? GetAiredDate().Value.Year : null,
                PremiereDate = GetAiredDate(),
                EndDate = GetAiredDate(),
                CommunityRating = GetRating(),
                Genres = GetGenres(),
                Studios = GetStudioNames(),
                ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, anime.MalId.ToString() } }
            };
        }

        public Season ToSeason()
        {
            var config = Plugin.Instance.Configuration;
            var duration = GetDuration(anime.Duration);
            return new Season
            {
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                OriginalTitle = GetPreferredTitle(config.OriginalTitlePreference, "romaji"),
                Overview = anime.Synopsis,
                ProductionYear = GetAiredDate().HasValue ? GetAiredDate().Value.Year : null,
                PremiereDate = GetAiredDate(),
                EndDate = GetAiredDate(),
                CommunityRating = GetRating(),
                RunTimeTicks = duration > 0 ? TimeSpan.FromMinutes(duration).Ticks : null,
                Genres = GetGenres(),
                Studios = GetStudioNames(),
                ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, anime.MalId.ToString() } },
            };
        }
    }
}
