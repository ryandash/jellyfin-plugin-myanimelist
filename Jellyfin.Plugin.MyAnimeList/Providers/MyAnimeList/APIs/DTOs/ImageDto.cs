using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class ImageDto
    {
        public string ImageUrl { get; set; }

        public string SmallImageUrl { get; set; }

        public string MediumImageUrl { get; set; }

        public string LargeImageUrl { get; set; }

        public string MaximumImageUrl { get; set; }

        public static ImageDto From(Image source)
        {
            if (source == null) return null;

            return new ImageDto
            {
                ImageUrl = string.IsNullOrWhiteSpace(source.ImageUrl) ? null : source.ImageUrl,
                SmallImageUrl = string.IsNullOrWhiteSpace(source.SmallImageUrl) ? null : source.SmallImageUrl,
                MediumImageUrl = string.IsNullOrWhiteSpace(source.MediumImageUrl) ? null : source.MediumImageUrl,
                LargeImageUrl = string.IsNullOrWhiteSpace(source.LargeImageUrl) ? null : source.LargeImageUrl,
                MaximumImageUrl = string.IsNullOrWhiteSpace(source.MaximumImageUrl) ? null : source.MaximumImageUrl
            };
        }
    }
}
