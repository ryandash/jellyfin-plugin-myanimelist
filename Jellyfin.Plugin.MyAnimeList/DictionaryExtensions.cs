using JikanDotNet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MyAnimeList
{
    public static class DictionaryExtensions
    {
        public static T GetOrDefault<TKey, T>(this IDictionary<TKey, T> dict, TKey key)
        {
            T value;
            if (dict.TryGetValue(key, out value))
                return value;

            return default(T);
        }

        public static Anime GetBestAnime(string animeName, ICollection<Anime> animeList)
        {
            Anime bestMatch = null;
            int minDistance = int.MaxValue;

            foreach (var anime in animeList)
            {
                var distance = levenshteinDistance(anime.Titles.First(t => t.Type == "Default").Title, animeName);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    bestMatch = anime;
                }
            }

            return bestMatch;
        }
        private static int levenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
                return string.IsNullOrEmpty(target) ? 0 : target.Length;

            if (string.IsNullOrEmpty(target))
                return source.Length;

            var sourceLength = source.Length;
            var targetLength = target.Length;
            var distance = new int[sourceLength + 1, targetLength + 1];

            for (var i = 0; i <= sourceLength; i++)
                distance[i, 0] = i;

            for (var j = 0; j <= targetLength; j++)
                distance[0, j] = j;

            for (var i = 1; i <= sourceLength; i++)
            {
                for (var j = 1; j <= targetLength; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            return distance[sourceLength, targetLength];
        }
    }
}
