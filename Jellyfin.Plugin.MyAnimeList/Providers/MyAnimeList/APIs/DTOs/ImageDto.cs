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

        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

        public static ImageDto From(Image source)
        {
            if (source == null) return null;

            return new ImageDto
            {
                ImageUrl = Normalize(source.ImageUrl),
                SmallImageUrl = Normalize(source.SmallImageUrl),
                MediumImageUrl = Normalize(source.MediumImageUrl),
                LargeImageUrl = Normalize(source.LargeImageUrl),
                MaximumImageUrl = Normalize(source.MaximumImageUrl)
            };
        }
    }
}
