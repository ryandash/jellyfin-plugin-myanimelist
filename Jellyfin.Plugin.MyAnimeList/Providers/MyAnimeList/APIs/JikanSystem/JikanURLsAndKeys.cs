namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem
{
    public static class URLs
    {
        public static string AnimeFull(long id) => $"anime/{id}/full";
        public static string AnimeCharacters(long id) => $"anime/{id}/characters";
        public static string AnimeEpisodes(long id) => $"anime/{id}/episodes";
        public static string AnimeSpecificEpisodes(long id, int ep) => $"anime/{id}/episodes/{ep}";
        public static string Character(long id) => $"characters/{id}";
        public static string People(long id) => $"people/{id}";
    }
    public static class Keys
    {
        public static string AnimeFull(long id) => $"anime:{id}";
        public static string AnimeEpisode(long id, int ep) => $"episode:{id}:{ep}";
        public static string Person(long id) => $"person:{id}";
        public static string SearchAnime(string query, bool nsfw) => $"search:{query}:nsfw:{nsfw}";
        public static string SearchCharacter(string query) => $"search:character:{query}";
        public static string SearchPerson(string query) => $"search:person:{query}";
        public static string AnimeEpisodes(long id) => $"episodes:{id}";
        public static string Character(long id) => $"character:{id}";
        public static string Characters(long id) => $"characters:{id}";
    }
}
