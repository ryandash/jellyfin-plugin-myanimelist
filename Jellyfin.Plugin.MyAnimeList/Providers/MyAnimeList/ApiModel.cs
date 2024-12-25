using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using SeasonTV = MediaBrowser.Controller.Entities.TV.Season;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    using Jellyfin.Plugin.MyAnimeList.Configuration;
    using Jellyfin.Plugin.MyAnimeList.Providers;
    using JikanDotNet;

    public static class NewJikan
    {
        public static Jikan _jikan = new Jikan();
    }

    public class EpisodeSearchResult
    {
        public AnimeEpisode episode { get; set; }

        public string GetPreferredTitle(TitlePreferenceType preference, string language)
        {
            return preference switch
            {
                TitlePreferenceType.Localized => language switch
                {
                    "en" => episode.Title ?? episode.TitleRomanji ?? episode.TitleJapanese,
                    "jap" => episode.TitleJapanese ?? episode.TitleRomanji ?? episode.Title,
                    _ => episode.Title ?? episode.TitleRomanji ?? episode.TitleJapanese
                },
                TitlePreferenceType.Japanese => episode.TitleJapanese ?? episode.TitleRomanji ?? episode.Title,
                _ => episode.Title ?? episode.TitleRomanji ?? episode.TitleJapanese
            };
        }

        public DateTime? GetDate() => episode.Aired;

        internal Episode ToEpisode(int totalDigits)
        {
            var config = Plugin.Instance.Configuration;
            var malIdStr = "#" + episode.MalId.ToString($"D{totalDigits}") + " - ";

            string GetFormattedTitle(TitlePreferenceType titlePreference) =>
                string.IsNullOrEmpty(GetPreferredTitle(titlePreference, "en"))
                ? null
                : malIdStr + GetPreferredTitle(titlePreference, "en");

            return new Episode
            {
                ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeListEP, episode.Url } },
                Name = GetFormattedTitle(config.TitlePreference),
                OriginalTitle = GetFormattedTitle(config.OriginalTitlePreference),
                ProductionYear = GetDate()?.Year,
                EndDate = GetDate(),
                RunTimeTicks = episode.Duration.HasValue ? TimeSpan.FromSeconds(episode.Duration.Value).Ticks : null,
                Overview = episode.Synopsis

            };
        }

        public RemoteSearchResult ToSearchResult()
        {
            var config = Plugin.Instance.Configuration;
            return new RemoteSearchResult
            {
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                ProductionYear = GetDate().HasValue ? GetDate().Value.Year : null,
                PremiereDate = GetDate(),
                SearchProviderName = ProviderNames.MyAnimeList,
                ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeListEP, episode.Url } }
            };
        }
    }

    public class AnimeSearchResult
    {
        public JikanDotNet.Anime anime;

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

            return anime.Titles.FirstOrDefault(t => t.Type.Equals(titleType, StringComparison.OrdinalIgnoreCase))?.Title;
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
        public List<string> GetStudioNames() => anime.Studios.Select(node => node.Name).ToList();
    }

    public class Anime : AnimeSearchResult
    {
        public ICollection<AnimeCharacter> characters { get; set; }

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
                    Name = x.va.Person.Name,
                    ImageUrl = x.va.Person.Images.JPG.MaximumImageUrl
                              ?? x.va.Person.Images.JPG.LargeImageUrl
                              ?? x.va.Person.Images.JPG.MediumImageUrl
                              ?? x.va.Person.Images.JPG.SmallImageUrl,
                    Role = x.edge.Role,
                    Type = PersonKind.Actor,
                    ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, x.va.Person.MalId.ToString() } }
                })
                .Take(config.MaxPeople > 0 ? config.MaxPeople : int.MaxValue)
                .ToList(); ;
        }

        public List<string> GetGenres()
        {
            var genres = anime.Genres.Select(g => g.Name);
            var config = Plugin.Instance.Configuration;

            if (config.AnimeDefaultGenre != AnimeDefaultGenreType.None)
            {
                genres = genres.Except(new[] { "Animation", "Anime" })
                               .Prepend(config.AnimeDefaultGenre.ToString());
            }

            return config.MaxGenres > 0 ? genres.Take(config.MaxGenres).ToList() : genres.ToList();
        }

        public Series ToSeries()
        {
            var config = Plugin.Instance.Configuration;
            var duration = GetDuration(anime.Duration);
            return new Series
            {
                Name = GetPreferredTitle(config.TitlePreference, "en"),
                OriginalTitle = GetPreferredTitle(config.OriginalTitlePreference, "en"),
                Overview = anime.Synopsis,
                ProductionYear = GetAiredDate().HasValue ? GetAiredDate().Value.Year : null,
                PremiereDate = GetAiredDate(),
                EndDate = GetAiredDate(),
                CommunityRating = GetRating(),
                RunTimeTicks = duration > 0 ? TimeSpan.FromMinutes(duration).Ticks : null,
                Genres = GetGenres().ToArray(),
                Studios = GetStudioNames().ToArray(),
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
                OriginalTitle = GetPreferredTitle(config.OriginalTitlePreference, "en"),
                Overview = anime.Synopsis,
                ProductionYear = GetAiredDate().HasValue ? GetAiredDate().Value.Year : null,
                PremiereDate = GetAiredDate(),
                EndDate = GetAiredDate(),
                CommunityRating = GetRating(),
                Genres = GetGenres().ToArray(),
                Studios = GetStudioNames().ToArray(),
                ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, anime.MalId.ToString() } }
            };
        }

        public SeasonTV ToSeason()
        {
            var config = Plugin.Instance.Configuration;
            var duration = GetDuration(anime.Duration);
            return new SeasonTV
            {
                OriginalTitle = GetPreferredTitle(config.OriginalTitlePreference, "en"),
                Overview = anime.Synopsis,
                ProductionYear = GetAiredDate().HasValue ? GetAiredDate().Value.Year : null,
                PremiereDate = GetAiredDate(),
                EndDate = GetAiredDate(),
                CommunityRating = GetRating(),
                RunTimeTicks = duration > 0 ? TimeSpan.FromMinutes(duration).Ticks : null,
                Genres = GetGenres().ToArray(),
                Studios = GetStudioNames().ToArray(),
                ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, anime.MalId.ToString() } },
            };
        }
    }
}
