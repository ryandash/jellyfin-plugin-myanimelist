using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class ImagesSetDto
    {
        public ImageDto JPG { get; set; }

        public static string GetImageUrl(ImageDto jpg)
        {
            return jpg.MaximumImageUrl ?? jpg.LargeImageUrl ?? jpg.MediumImageUrl ?? jpg.ImageUrl ?? jpg.SmallImageUrl;
        }

        public static ImagesSetDto From(ImagesSet source)
        {
            if (source is null) return null;

            return new ImagesSetDto
            {
                JPG = ImageDto.From(source.JPG)
            };
        }
    }
}
