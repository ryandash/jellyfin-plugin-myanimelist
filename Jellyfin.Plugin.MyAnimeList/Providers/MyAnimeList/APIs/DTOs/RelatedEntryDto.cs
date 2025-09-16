using JikanDotNet;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class RelatedEntryDto
    {
        public string Relation { get; set; }
        public List<long> Entry { get; set; }

        internal static RelatedEntryDto From(RelatedEntry entry, int unused = 0)
        {
            if (entry == null) return null;

            var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Prequel",
                "Sequel",
                "Side Story"
            };

            if (!allowedTypes.Contains(entry.Relation))
                return null;

            return new RelatedEntryDto
            {
                Relation = entry.Relation,
                Entry = entry.Entry?.Select(e => e.MalId).ToList()
            };
        }
    }
}
