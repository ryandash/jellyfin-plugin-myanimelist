using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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

        private static readonly Regex YearRegex = new Regex(
            @"\((\d{4})\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex EpisodeTitleRegex = new Regex(
            @"(?ix)
                ^
                (?<title>.*?)
                \s*
                (?:[-–—]\s*)?
                (?:
                    S00(?:E\d{1,4})?
                  | EP?\s*\.?\s*\d{1,4}
                  | special(?:\s+episode)?
                  | ova
                  | oad
                  | ona
                  | extra
                  | bonus
                )
                \s*
                [-–—:]\s*
                (?<episodeTitle>.+?)
                $
            ",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex BracketJunkRegex = new Regex(
            @"\[[^\]]*\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ParenthesesJunkRegex = new Regex(
            @"\([^)]*\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex TechnicalJunkRegex = new Regex(
            @"(?ix)
                \b(?:2160|1440|1080|720|576|480)[pi]\b
              | \b(?:x264|x265|h264|h265|hevc|av1|avc)\b
              | \b(?:aac|ac3|eac3|dts|flac|opus|vorbis|mp3|truehd|atmos)\b
              | \b(?:web[-_. ]?dl|web[-_. ]?rip|webrip|bluray|blu[-_. ]?ray|bdrip|brrip|dvdrip|hdtv|hdrip|remux)\b
              | \b(?:hdr10\+?|dolby[-_. ]?vision|10[-_. ]?bit|8[-_. ]?bit|hi10p)\b
            ",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex ReleaseJunkRegex = new Regex(
            @"(?ix)
                \b(?:proper|repack|rerip|limited|complete|batch|uncensored|censored|retail|extended)\b
              | \b(?:dual[-_. ]?audio|multi[-_. ]?audio|multi[-_. ]?sub|dubbed|subbed)\b
              | \b[0-9A-F]{8}\b
            ",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex WhitespaceRegex = new Regex(
            @"\s+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static void ExtractTitleAndYear(string searchTerm, out string title, out string year, bool specialEpisode = false)
        {
            title = null;
            year = null;

            if (string.IsNullOrWhiteSpace(searchTerm))
                return;

            string parseTerm = searchTerm.Trim();

            var yearMatch = YearRegex.Match(parseTerm);

            if (yearMatch.Success)
            {
                year = yearMatch.Groups[1].Value;
            }

            parseTerm = BracketJunkRegex.Replace(parseTerm, " ");
            parseTerm = ParenthesesJunkRegex.Replace(parseTerm, " ");
            parseTerm = TechnicalJunkRegex.Replace(parseTerm, " ");
            parseTerm = ReleaseJunkRegex.Replace(parseTerm, " ");
            parseTerm = WhitespaceRegex.Replace(parseTerm, " ").Trim();

            if (specialEpisode)
            {
                var episodeMatch = EpisodeTitleRegex.Match(parseTerm);
                if (episodeMatch.Success)
                {
                    title = episodeMatch.Groups["episodeTitle"].Value.Trim();
                    return;
                }
            }

            title = parseTerm;
        }
    }
}
