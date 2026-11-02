using Jellyfin.Plugin.MyAnimeList.Configuration;
using Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs;
using MediaBrowser.Model.Providers;
using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class AnimeSearchResult : JellyfinEntityMapperBase
    {
        public AnimeFullCacheDto Anime { get; set; }

        public string GetPreferredTitle(TitlePreferenceType preference)
        {
            return GetPreferredTitle(
                Anime?.Titles,
                preference,
                title => title?.Type,
                title => title?.Title);
        }

        public DateTime? GetAiredDate(bool isStartDate = true) => isStartDate ? Anime.Aired?.From : Anime.Aired?.To;

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
}
