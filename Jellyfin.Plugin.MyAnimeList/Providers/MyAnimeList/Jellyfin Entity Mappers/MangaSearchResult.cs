using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class MangaSearchResult : JellyfinEntityMapperBase
    {
        public MangaFullCacheDto Manga { get; set; }

        public string GetPreferredTitle(TitlePreferenceType preference)
        {
            return GetPreferredTitle(
                Manga?.Titles,
                preference,
                title => title?.Type,
                title => title?.Title);
        }

        public DateTime? GetAiredDate(bool isStartDate = true) => isStartDate ? Manga.Published?.From : Manga.Published?.To;

        public RemoteSearchResult ToSearchResult()
        {
            var published = GetAiredDate();
            return new RemoteSearchResult
            {
                Name = GetPreferredTitle(Config.TitlePreference),
                ProductionYear = published.HasValue ? published.Value.Year : null,
                PremiereDate = published,
                ImageUrl = Manga.Images.Image,
                SearchProviderName = ProviderNames.MyAnimeList,
                ProviderIds = new Dictionary<string, string> { { ProviderNames.MyAnimeList, Manga.MalId.ToString() } }
            };
        }
    }
}
