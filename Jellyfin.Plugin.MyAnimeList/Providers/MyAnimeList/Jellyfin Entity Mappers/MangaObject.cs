using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class MangaObject : MangaSearchResult
    {
        public List<AnimeCharacterDto> Characters { get; set; }

        public List<PersonInfo> GetPeopleInfo()
        {
            var people = new List<PersonInfo>();

            foreach (var entry in Characters ?? Enumerable.Empty<AnimeCharacterDto>())
            {
                if (entry?.Character == null || string.IsNullOrWhiteSpace(entry.Character.Url))
                    continue;

                people.Add(new PersonInfo
                {
                    Name = entry.Character.Name,
                    Role = entry.Role,
                    Type = default,
                    ImageUrl = entry.Character.Images?.Image,
                    ProviderIds = new Dictionary<string, string>
                    {
                        { ProviderNames.MyAnimeList, entry.Character.Url }
                    }
                });
            }

            return people;
        }

        public Book ToManga(BookInfo info)
        {
            var metadata = Config.BookMetadata;

            var manga = new Book();

            if (metadata.SeriesName)
                manga.SeriesName = info.SeriesName;

            if (metadata.Name)
                manga.Name = GetPreferredTitle(Config.TitlePreference);

            var overview = new StringBuilder();

            if (metadata.Chapters)
                overview.Append($"Chapters: {Manga.Chapters}\n\n");

            if (metadata.Volumes)
                overview.Append($"Volumes: {Manga.Volumes}\n\n");

            if (metadata.Overview)
                overview.Append(Manga.Synopsis);

            manga.Overview = overview.ToString().Trim();

            if (metadata.ProductionYear)
                manga.ProductionYear = GetAiredDate()?.Year;

            if (metadata.CommunityRating)
                manga.CommunityRating = Manga.Score;

            if (metadata.Genres)
                manga.Genres = (Config.MaxGenres > 0 ? Manga.Genres?.Take(Config.MaxGenres) : Manga.Genres).ToArray();

            if (metadata.Studios)
                manga.Studios = Manga.Serializations;

            if (metadata.Tags)
                manga.Tags = Manga.Tags;

            return manga;
        }
    }
}
