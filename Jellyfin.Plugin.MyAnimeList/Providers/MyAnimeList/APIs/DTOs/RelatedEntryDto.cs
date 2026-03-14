using System;
using System.Collections.Generic;
using System.Linq;
using JikanDotNet;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class RelatedEntryDto
    {
        public string Relation { get; set; }
        public List<long> Entry { get; set; }
        public static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Prequel",
            "Sequel",
            "Side Story"
        };
        private static readonly List<RelatedEntryDto> Empty = new();

        public static List<RelatedEntryDto> FilterRelations(ICollection<RelatedEntry> relations)
        {
            return relations.Where(r => r != null && AllowedTypes.Contains(r.Relation))
                    .Select(From).ToList() ?? Empty;
        }

        internal static RelatedEntryDto From(RelatedEntry entry)
        {
            return new RelatedEntryDto
            {
                Relation = entry.Relation,
                Entry = entry.Entry.Select(e => e.MalId).Where(id => id > 0).ToList()
            };
        }
    }
}
