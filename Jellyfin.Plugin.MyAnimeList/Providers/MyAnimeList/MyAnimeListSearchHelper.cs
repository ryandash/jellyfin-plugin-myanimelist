using JikanDotNet;
using MediaBrowser.Controller.Providers;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList
{
    public class MyAnimeListSearchHelper
    {
        private static readonly Jikan _jikan = JikanSingleton.Instance;

        public static async Task<long?> GetAnimeIdAsync(ItemLookupInfo info, CancellationToken cancellationToken)
        {
            string malId = info switch
            {
                EpisodeInfo episodeInfo => episodeInfo.SeasonProviderIds.GetOrDefault(ProviderNames.MyAnimeList),
                _ => info.ProviderIds.GetOrDefault(ProviderNames.MyAnimeList)
            };

            if (!string.IsNullOrEmpty(malId))
                return long.Parse(malId);

            string searchName = GetSearchName(info);
            long? malIdFromName = await NameToMalIdAsync(searchName, info is MovieInfo);

            if ((info is SeasonInfo || info is EpisodeInfo) && malIdFromName.HasValue)
            {
                return await GetAnimeBySeasonAsync(malIdFromName.Value, info.IndexNumber ?? 1, cancellationToken);
            }

            return malIdFromName;
        }

        private static string GetSearchName(ItemLookupInfo info)
        {
            string[] splitPath = info.Path.Split('\\');
            return info switch
            {
                SeasonInfo => splitPath[^1].Contains("season", StringComparison.OrdinalIgnoreCase)
                    ? splitPath[^2]
                    : splitPath[^1],
                EpisodeInfo => splitPath[^2].Contains("season", StringComparison.OrdinalIgnoreCase)
                    ? splitPath[^3]
                    : splitPath[^2],
                _ => info.Name
            };
        }

        public static async Task<long?> NameToMalIdAsync(string searchName, bool isMovie)
        {
            searchName = Regex.Replace(searchName, @"(\s|\.)S[0-9]{1,2}", string.Empty); // Remove season designation
            searchName = Regex.Replace(searchName, @"\s*~(\w|[0-9]|\s)+~", string.Empty); // Remove ALT NAME
            searchName = Regex.Replace(searchName.Trim(), @"\((\w|[0-9]|\s)+\)$", string.Empty); // Remove native name
            searchName = Regex.Replace(searchName, @"\s?&\s?", " and "); // Replace "&" with "and"
            searchName = Regex.Replace(searchName, @"#", " "); // Replace "#" with space
            searchName = Regex.Replace(searchName.Trim(), @"\([0-9]{4}\)\s*\[(\w|[0-9]|-)+\]$", string.Empty); // Truncate Jellyfin folder format

            return await MALNameSearch.GetFirstAnimeID(searchName, isMovie);
        }

        private static async Task<long?> GetAnimeBySeasonAsync(long malId, int seasonNumber, CancellationToken cancellationToken)
        {
            for (int currentSeason = 1; currentSeason < seasonNumber; currentSeason++)
            {
                var relations = await _jikan.GetAnimeRelationsAsync(malId, cancellationToken).ConfigureAwait(false);
                var sequelRelation = relations?.Data?.FirstOrDefault(r => r.Relation.Equals("Sequel", StringComparison.OrdinalIgnoreCase));

                if (sequelRelation == null) break;

                var malUrl = sequelRelation.Entry.FirstOrDefault();
                if (malUrl == null) break;

                malId = malUrl.MalId;

                var anime = (await _jikan.GetAnimeAsync(malId, cancellationToken).ConfigureAwait(false)).Data;

                if (anime.Titles.Any(t => t.Title.Contains("part ", StringComparison.OrdinalIgnoreCase)) ||
                    !(anime.Type.Equals("TV", StringComparison.OrdinalIgnoreCase) || anime.Type.Equals("ONA", StringComparison.OrdinalIgnoreCase)))
                {
                    seasonNumber++;
                }
            }

            return malId;
        }
    }
}
