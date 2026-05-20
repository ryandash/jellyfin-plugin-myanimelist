using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
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

        internal Episode ToEpisode(Episode existing, EpisodeInfo info, int totalDigits)
        {
            existing.IndexNumber ??= info.IndexNumber;
            existing.ParentIndexNumber ??= info.ParentIndexNumber;
            existing.IndexNumberEnd ??= info.IndexNumberEnd;

            existing.SetProviderId(ProviderNames.MyAnimeList, episode.Url);

            if (string.IsNullOrWhiteSpace(existing.Name))
                existing.Name = GetPreferredTitle(config.TitlePreference, "en");

            if (string.IsNullOrWhiteSpace(existing.OriginalTitle))
                existing.OriginalTitle = GetPreferredTitle(config.OriginalTitlePreference, "romaji");

            var aired = episode.Aired;
            existing.ProductionYear ??= aired?.Year;
            existing.EndDate ??= aired;
            existing.RunTimeTicks ??= episode.Duration.HasValue
                ? TimeSpan.FromSeconds(episode.Duration.Value).Ticks
                : null;

            if (string.IsNullOrWhiteSpace(existing.Overview))
                existing.Overview = episode.Synopsis;

            float? rating = (float?)episode.Score;
            if (!existing.CommunityRating.HasValue && rating > 0)
                existing.CommunityRating = rating;

            return existing;
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

        public List<PersonInfo> GetPeopleInfo(IEnumerable<PersonInfo> existingPeople)
        {
            var people = existingPeople?.ToList() ?? new List<PersonInfo>();

            var lookup = new Dictionary<(string Name, string Role), PersonInfo>();

            foreach (var p in people)
            {
                var key = (Normalize(p.Name), Normalize(p.Role));

                if (!lookup.ContainsKey(key))
                    lookup[key] = p;
            }

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

                    if (lookup.TryGetValue(key, out var existing))
                    {
                        existing.ProviderIds ??= new Dictionary<string, string>();

                        existing.ProviderIds[ProviderNames.MyAnimeList] = va.Person.MalId.ToString();

                        if (string.IsNullOrWhiteSpace(existing.ImageUrl))
                            existing.ImageUrl = ImagesSetDto.GetImageUrl(va.Person.Images.JPG);

                        continue;
                    }

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
                    lookup[key] = newPerson;
                }
            }

            int limit = config.MaxPeople > 0 ? config.MaxPeople : int.MaxValue;

            return people.Take(limit).ToList();
        }

        public string[] GetGenres(string[] existingGenres)
        {
            var malGenres = anime?.Genres?
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                ?? Enumerable.Empty<string>();

            var merged = (existingGenres ?? Array.Empty<string>())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .Concat(malGenres)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            if (config.MaxGenres > 0)
                merged = merged.Take(config.MaxGenres);

            return merged.ToArray();
        }

        public string[] GetStudioNames(string[] existingStudios)
        {
            var malStudios = anime?.Studios?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                ?? Enumerable.Empty<string>();

            var merged = (existingStudios ?? Array.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Concat(malStudios)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            return merged.ToArray();
        }

        public Series ToSeries(Series existing, SeriesInfo info)
        {
            existing.IndexNumber ??= info.IndexNumber;
            existing.ParentIndexNumber ??= info.ParentIndexNumber;

            existing.SetProviderId(ProviderNames.MyAnimeList, anime.MalId.ToString());

            existing.Name ??= GetPreferredTitle(config.TitlePreference, "en");
            existing.OriginalTitle ??= GetPreferredTitle(config.OriginalTitlePreference, "romaji");

            existing.Overview ??= anime.Synopsis;

            var aired = GetAiredDate();
            existing.ProductionYear ??= aired?.Year;
            existing.PremiereDate ??= aired;
            existing.EndDate ??= aired;

            if (!existing.CommunityRating.HasValue && GetRating() > 0)
                existing.CommunityRating = GetRating();

            var duration = GetDuration(anime.Duration);
            if (existing.RunTimeTicks == null && duration > 0)
                existing.RunTimeTicks = TimeSpan.FromMinutes(duration).Ticks;

            existing.Genres = GetGenres(existing.Genres);
            existing.Studios = GetStudioNames(existing.Studios);

            existing.Status ??= anime.Status switch
            {
                "Finished Airing" => SeriesStatus.Ended,
                "Currently Airing" => SeriesStatus.Continuing,
                "Not yet aired" => SeriesStatus.Unreleased,
                _ => SeriesStatus.Unreleased
            };

            return existing;
        }

        public Movie ToMovie(Movie existing, MovieInfo info)
        {
            existing.IndexNumber ??= info.IndexNumber;
            existing.ParentIndexNumber ??= info.ParentIndexNumber;

            existing.SetProviderId(ProviderNames.MyAnimeList, anime.MalId.ToString());

            existing.Name ??= GetPreferredTitle(config.TitlePreference, "en");
            existing.OriginalTitle ??= GetPreferredTitle(config.OriginalTitlePreference, "romaji");

            existing.Overview ??= anime.Synopsis;

            var aired = GetAiredDate();
            existing.ProductionYear ??= aired?.Year;
            existing.PremiereDate ??= aired;
            existing.EndDate ??= aired;

            if (!existing.CommunityRating.HasValue && GetRating() > 0)
                existing.CommunityRating = GetRating();

            existing.Genres = GetGenres(existing.Genres);
            existing.Studios = GetStudioNames(existing.Studios);

            return existing;
        }

        public Season ToSeason(Season existing, SeasonInfo info)
        {
            existing.IndexNumber ??= info.IndexNumber;
            existing.ParentIndexNumber ??= info.ParentIndexNumber;

            existing.SetProviderId(ProviderNames.MyAnimeList, anime.MalId.ToString());

            existing.Name ??= GetPreferredTitle(config.TitlePreference, "en");
            existing.OriginalTitle ??= GetPreferredTitle(config.OriginalTitlePreference, "romaji");

            existing.Overview ??= anime.Synopsis;

            var aired = GetAiredDate();
            existing.ProductionYear ??= aired?.Year;
            existing.PremiereDate ??= aired;
            existing.EndDate ??= aired;

            if (!existing.CommunityRating.HasValue && GetRating() > 0)
                existing.CommunityRating = GetRating();

            var duration = GetDuration(anime.Duration);
            if (existing.RunTimeTicks == null && duration > 0)
                existing.RunTimeTicks = TimeSpan.FromMinutes(duration).Ticks;

            existing.Genres = GetGenres(existing.Genres);
            existing.Studios = GetStudioNames(existing.Studios);

            return existing;
        }
    }
}
