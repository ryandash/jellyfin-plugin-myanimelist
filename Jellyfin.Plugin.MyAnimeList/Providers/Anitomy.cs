using AnitomySharp;
using System.Linq;

namespace Jellyfin.Plugin.MyAnimeList.Anitomy
{
    public class AnitomyHelper
    {
        public string AnimeTitle { get; }
        public string EpisodeTitle { get; }
        public int Episode { get; }
        public int Season { get; }

        public AnitomyHelper(string fileName)
        {
            var elements = AnitomySharp.AnitomySharp.Parse(fileName);
            AnimeTitle = elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementAnimeTitle)?.Value;
            EpisodeTitle = elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementEpisodeTitle)?.Value;
            Episode = int.TryParse(elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementEpisodeNumber)?.Value, out int episode) ? episode : 0;
            Season = int.TryParse(elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementAnimeSeason)?.Value, out int season) ? season : 0;
        }
    }
}
