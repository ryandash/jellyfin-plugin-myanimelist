using MediaBrowser.Controller.Providers;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.Helpers
{
    public class MalIdResolver
    {
        public static long? TryGetDirectMalId(ItemLookupInfo info)
        {
            if (info is EpisodeInfo e && e.IndexNumber == 0)
                return null;

            string id = info switch
            {
                EpisodeInfo ep => ep.SeasonProviderIds.GetOrDefault(ProviderNames.MyAnimeList),
                _ => info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList)
            };

            return long.TryParse(id, out var value) ? value : null;
        }

        public static long? TryGetSeriesMalId(ItemLookupInfo info, out int? confidence)
        {
            confidence = null;

            if (info is SeasonInfo season &&
                long.TryParse(season.SeriesProviderIds.GetOrDefault(ProviderNames.MyAnimeList), out var seasonId))
            {
                confidence = 100;
                return seasonId;
            }

            if (info is EpisodeInfo episode &&
                long.TryParse(episode.SeriesProviderIds.GetOrDefault(ProviderNames.MyAnimeList), out var epId))
            {
                confidence = 100;
                return epId;
            }

            return null;
        }

        private static readonly Regex MalIdRegex = new Regex(@"\[mal-(\d+)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        public static long? ExtractMalIdFromSearchName(string searchName)
        {
            var match = MalIdRegex.Match(searchName);
            if (!match.Success)
                return null;

            if (long.TryParse(match.Groups[1].Value, out var malId))
                return malId;

            return null;
        }
    }
}
