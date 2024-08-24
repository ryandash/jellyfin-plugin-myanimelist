using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using Jellyfin.Plugin.MyAnimeList.Configuration;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    using Jellyfin.Plugin.MyAnimeList.Configuration;
    using Jellyfin.Plugin.MyAnimeList.Providers;
    using JikanDotNet;
    using System.Collections.Generic;

    /// <summary>
    /// A slimmed down version of Media to avoid confusion and reduce
    /// the size of responses when searching.
    /// </summary>
    public class MediaSearchResult
    {
        public Anime anime;

        /// <summary>
        /// Get the title in configured language
        /// </summary>
        /// <param name="language"></param>
        /// <returns></returns>
        public string GetPreferredTitle(TitlePreferenceType preference, string language)
        {
            string titleType = preference switch
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

            return anime.Titles.FirstOrDefault(t => t.Type.Equals(titleType, StringComparison.OrdinalIgnoreCase)).Title;
        }

        /// <summary>
        /// Get the highest quality image url
        /// </summary>
        /// <returns></returns>
        public string GetImageUrl()
        {
            return anime.Images.JPG.MaximumImageUrl ?? anime.Images.JPG.LargeImageUrl ?? anime.Images.JPG.MediumImageUrl ?? anime.Images.JPG.SmallImageUrl;
        }

        /// <summary>
        /// Returns the start date as a DateTime object or null if not available
        /// </summary>
        /// <returns></returns>
        public DateTime? GetStartDate()
        {
            return anime.Aired.From;
        }

        /// <summary>
        /// Convert a Media/MediaSearchResult object to a RemoteSearchResult
        /// </summary>
        /// <returns></returns>
        public RemoteSearchResult ToSearchResult()
        {
            PluginConfiguration config = Plugin.Instance.Configuration;
            return new RemoteSearchResult
            {
                Name = this.GetPreferredTitle(config.TitlePreference, "en"),
                ProductionYear = anime.Aired.From.Value.Year,
                PremiereDate = this.GetStartDate(),
                ImageUrl = this.GetImageUrl(),
                SearchProviderName = ProviderNames.MyAnimeList,
                ProviderIds = new Dictionary<string, string>() { { ProviderNames.MyAnimeList, anime.MalId.ToString() } }
            };
        }
    }

    public class Media : MediaSearchResult
    {
        public ICollection<AnimeCharacter> characters { get; set; }
        /// <summary>
        /// Get the rating, normalized to 1-10
        /// </summary>
        /// <returns></returns>
        public float GetRating()
        {
            return (float)((anime.Score ?? 0) / 10f);
        }

        /// <summary>
        /// Returns the end date as a DateTime object or null if not available
        /// </summary>
        /// <returns></returns>
        public DateTime? GetEndDate()
        {
            return anime.Aired.To;
        }

        private int GetDuration(string duration)
        {
            int totalMinutes = 0;
            var parts = duration.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int value))
                {
                    if (i + 1 < parts.Length)
                    {
                        if (parts[i + 1].StartsWith("hr", StringComparison.OrdinalIgnoreCase))
                        {
                            totalMinutes += value * 60;
                            i++; // Skip next part as it's already processed
                        }
                        else if (parts[i + 1].StartsWith("min", StringComparison.OrdinalIgnoreCase))
                        {
                            totalMinutes += value;
                            i++; // Skip next part as it's already processed
                        }
                    }
                }
            }

            return totalMinutes;
        }

        /// <summary>
        /// Returns a list of studio names
        /// </summary>
        /// <returns></returns>
        public List<string> GetStudioNames()
        {
            List<string> results = new List<string>();
            foreach (MalUrl node in anime.Studios)
            {
                results.Add(node.Name);
            }
            return results;
        }

        /// <summary>
        /// Returns a list of PersonInfo for voice actors
        /// </summary>
        /// <returns></returns>
        public List<PersonInfo> GetPeopleInfo()
        {
            PluginConfiguration config = Plugin.Instance.Configuration;
            List<PersonInfo> lpi = new List<PersonInfo>();
            foreach (AnimeCharacter edge in characters)
            {
                foreach (VoiceActorEntry va in edge.VoiceActors)
                {
                    if (config.PersonLanguageFilterPreference != LanguageFilterType.All)
                    {
                        if (config.PersonLanguageFilterPreference == LanguageFilterType.Japanese && va.Language != "Japanese")
                        {
                            continue;
                        }
                        if (config.PersonLanguageFilterPreference == LanguageFilterType.Localized && va.Language == "Japanese")
                        {
                            continue;
                        }
                    }
                    PeopleHelper.AddPerson(lpi, new PersonInfo
                    {
                        Name = va.Person.Name,
                        ImageUrl = va.Person.Images.JPG.MaximumImageUrl ?? va.Person.Images.JPG.LargeImageUrl ?? va.Person.Images.JPG.MediumImageUrl ?? va.Person.Images.JPG.SmallImageUrl,
                        Role = edge.Role,
                        Type = PersonKind.Actor,
                        ProviderIds = new Dictionary<string, string>() { { ProviderNames.MyAnimeList, va.Person.MalId.ToString() } }
                    });
                }
            }

            if (config.MaxPeople > 0)
            {
                lpi = lpi.Take(config.MaxPeople).ToList();
            }

            return lpi;
        }

        /// <summary>
        /// Returns a list of genres
        /// </summary>
        /// <returns></returns>
        public List<string> GetGenres()
        {
            IEnumerable<string> genres = anime.Genres.Select(g => g.Name);
            PluginConfiguration config = Plugin.Instance.Configuration;
            
            if (config.AnimeDefaultGenre != AnimeDefaultGenreType.None)
            {
                genres = genres
                    .Except(new[] { "Animation", "Anime" })
                    .Prepend(config.AnimeDefaultGenre.ToString());
            }

            if (config.MaxGenres > 0)
            {
                genres = genres.Take(config.MaxGenres);
            }
            return genres.ToList();
        }

        /// <summary>
        /// Convert a Media object to a Series
        /// </summary>
        /// <returns></returns>
        public Series ToSeries()
        {
            PluginConfiguration config = Plugin.Instance.Configuration;
            int duration = GetDuration(anime.Duration);
            var result = new Series
            {
                Name = this.GetPreferredTitle(config.TitlePreference, "en"),
                OriginalTitle = this.GetPreferredTitle(config.OriginalTitlePreference, "en"),
                Overview = anime.Synopsis,
                ProductionYear = anime.Aired.From.Value.Year,
                PremiereDate = this.GetStartDate(),
                EndDate = this.GetEndDate(),
                CommunityRating = this.GetRating(),
                RunTimeTicks = duration != 0 ? TimeSpan.FromMinutes(duration).Ticks : (long?)null,
                Genres = this.GetGenres().ToArray(),
                Studios = this.GetStudioNames().ToArray(),
                ProviderIds = new Dictionary<string, string>() { { ProviderNames.MyAnimeList, anime.MalId.ToString() } }
            };

            if (anime.Status == "Finished Airing")
            {
                result.Status = SeriesStatus.Ended;
            }
            else if (anime.Status == "Currently Airing")
            {
                result.Status = SeriesStatus.Continuing;
            }
            else if (anime.Status == "Not yet aired")
            {
                result.Status = SeriesStatus.Unreleased;
            }

            return result;
        }

        /// <summary>
        /// Convert a Media object to a Movie
        /// </summary>
        /// <returns></returns>
        public Movie ToMovie()
        {
            PluginConfiguration config = Plugin.Instance.Configuration;
            return new Movie
            {
                Name = this.GetPreferredTitle(config.TitlePreference, "en"),
                OriginalTitle = this.GetPreferredTitle(config.OriginalTitlePreference, "en"),
                Overview = anime.Synopsis,
                ProductionYear = anime.Aired.From.Value.Year,
                PremiereDate = this.GetStartDate(),
                EndDate = this.GetEndDate(),
                CommunityRating = this.GetRating(),
                Genres = this.GetGenres().ToArray(),
                Studios = this.GetStudioNames().ToArray(),
                ProviderIds = new Dictionary<string, string>() { { ProviderNames.MyAnimeList, anime.MalId.ToString() } }
            };
        }
    }
}
