using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class ImagesSetDto
    {
        public string Image { get; set; }

        public static ImagesSetDto From(ImagesSet source)
        {
            var jpg = source?.JPG;

            return jpg == null ? null : new ImagesSetDto { Image = jpg.MaximumImageUrl ?? jpg.LargeImageUrl ?? jpg.MediumImageUrl ?? jpg.ImageUrl ?? jpg.SmallImageUrl };
        }
    }
}
