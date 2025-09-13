using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class TitleEntryDto
    {
        public string Type { get; set; }

        public string Title { get; set; }

        public static TitleEntryDto From(TitleEntry source) =>
            source == null ? null : new TitleEntryDto { Type = source.Type, Title = source.Title };
    }
}
