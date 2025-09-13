using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class MalUrlDto
    {
        public long MalId { get; set; }
        public string Url { get; set; }
        public string Name { get; set; }

        public static MalUrlDto From(MalUrl source) =>
            source == null ? null : new MalUrlDto { MalId = source.MalId, Name = source.Name, Url = source.Url };
    }
}
