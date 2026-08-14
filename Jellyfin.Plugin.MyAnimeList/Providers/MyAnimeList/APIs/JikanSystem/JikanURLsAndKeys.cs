namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem
{
    public static class URLs
    {
        public static string AnimeFull(long id) => $"anime/{id}/full";
        public static string AnimeCharacters(long id) => $"anime/{id}/characters";
        public static string AnimeEpisodes(long id) => $"anime/{id}/episodes";
        public static string AnimeSpecificEpisodes(long id, int ep) => $"anime/{id}/episodes/{ep}";
        public static string Character(long id) => $"characters/{id}";
        public static string Person(long id) => $"people/{id}";
    }
    public static class Keys
    {
        private static string NormalizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return string.Empty;
            }

            return string.Join(' ', query.Trim().ToLowerInvariant().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
        }

        public static string AnimeFull(long id) => $"anime:{id}";
        public static string AnimeEpisode(long id, int ep) => $"episode:{id}:{ep}";
        public static string Person(long id) => $"person:{id}";
        public static string SearchAnime(string query, bool nsfw) => $"search:{NormalizeQuery(query)}:nsfw:{nsfw}";
        public static string SearchCharacter(string query) => $"search:character:{NormalizeQuery(query)}";
        public static string SearchPerson(string query) => $"search:person:{NormalizeQuery(query)}";
        public static string AnimeEpisodes(long id) => $"episodes:{id}";
        public static string Character(long id) => $"character:{id}";
        public static string Characters(long id) => $"characters:{id}";
    }
}
