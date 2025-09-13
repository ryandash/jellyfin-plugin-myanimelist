using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class ImagesSetDto
    {
        public ImageDto JPG { get; set; }

        public static ImagesSetDto From(ImagesSet source)
        {
            if (source == null) return null;

            return new ImagesSetDto
            {
                JPG = ImageDto.From(source.JPG)
            };
        }
    }
}
