using AnitomySharp;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static AnitomySharp.AnitomySharp;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.Helpers
{
    public class NameBuilder
    {
        private readonly string[] _libraryRoots;

        public NameBuilder(ILibraryManager libraryManager)
        {
            _libraryRoots = libraryManager
                .GetVirtualFolders()
                .SelectMany(v => v.Locations)
                .Where(l => !string.IsNullOrEmpty(l))
                .Select(l => l.TrimEnd(Path.DirectorySeparatorChar))
                .OrderByDescending(l => l.Length)
                .ToArray();
        }

        public (string, bool) GetSearchName(ItemLookupInfo info, ILogger _log, bool enableDebug)
        {
            if (string.IsNullOrEmpty(info.Path))
                return (info.Name, false);
            string relativePath = StripLibraryPath(info.Path, _log, enableDebug);
            var splitPath = relativePath.Split(Path.DirectorySeparatorChar);
            if (enableDebug) _log.LogInformation($"Remaining strings: \"{string.Join("\", \"", splitPath)}\"");
            if (splitPath.Length < 1) return (info.Name, false);
            int index = splitPath.Length - 1;

            switch (info)
            {
                case EpisodeInfo episode:
                    {
                        if (episode.ParentIndexNumber == 0)
                        {
                            return (info.Name, true);
                        }

                        return (info.Name, false);
                    }

                case SeasonInfo:
                    return (splitPath[index].Contains("season", StringComparison.OrdinalIgnoreCase) ||
                           splitPath[index].Contains("special", StringComparison.OrdinalIgnoreCase)
                        ? splitPath[Math.Max(0, index - 1)]
                        : splitPath[index], false);

                case SeriesInfo:
                    return (splitPath[index], false);

                default:
                    return (info.Name, false);
            }
        }

        private string StripLibraryPath(string itemPath, ILogger log, bool enableDebug)
        {
            if (string.IsNullOrEmpty(itemPath))
                return itemPath;

            foreach (var root in _libraryRoots)
            {
                if (itemPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    if (enableDebug) log.LogInformation("Removed library root '{Root}' from '{Path}'", root, itemPath);

                    int start = root.Length;
                    if (start < itemPath.Length && itemPath[start] == Path.DirectorySeparatorChar)
                        start++;

                    return itemPath[start..];
                }
            }

            return itemPath;
        }

        public static void ExtractTitleAndYear(string searchTerm, out string title, out string year, bool specialEpisode = false)
        {
            title = null;
            year = null;

            IEnumerable<Element> elements = Parse(searchTerm);

            string episodeTitle = null;
            string animeTitle = null;

            foreach (var e in elements)
            {
                switch (e.Category)
                {
                    case Element.ElementCategory.ElementEpisodeTitle:
                        episodeTitle ??= e.Value;
                        break;

                    case Element.ElementCategory.ElementAnimeTitle:
                        animeTitle ??= e.Value;
                        break;

                    case Element.ElementCategory.ElementAnimeYear:
                        year ??= e.Value;
                        break;
                }

                if (year is not null && episodeTitle is not null && animeTitle is not null)
                    break;
            }

            title = (specialEpisode && !string.IsNullOrWhiteSpace(episodeTitle)) ? episodeTitle : animeTitle;
        }
    }
}
