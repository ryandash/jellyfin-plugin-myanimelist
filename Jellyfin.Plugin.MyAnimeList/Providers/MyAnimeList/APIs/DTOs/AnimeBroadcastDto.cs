using JikanDotNet;
using System;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class AnimeBroadcastDto
    {
        public DayOfWeek[] AirDays { get; set; }
        public string AirTime { get; set; }

        public static AnimeBroadcastDto From(AnimeBroadcast source)
        {
            if (source == null)
                return null;

            return new AnimeBroadcastDto
            {
                AirDays = ParseAirDays(source.Day),
                AirTime = ConvertToLocalTimeString(source.Time, source.Timezone)
            };
        }

        private static DayOfWeek[] ParseAirDays(string day)
        {
            return day switch
            {
                "Mondays" => [DayOfWeek.Monday],
                "Tuesdays" => [DayOfWeek.Tuesday],
                "Wednesdays" => [DayOfWeek.Wednesday],
                "Thursdays" => [DayOfWeek.Thursday],
                "Fridays" => [DayOfWeek.Friday],
                "Saturdays" => [DayOfWeek.Saturday],
                "Sundays" => [DayOfWeek.Sunday],
                _ => null
            };
        }

        private static string ConvertToLocalTimeString(string time, string timezone)
        {
            if (string.IsNullOrWhiteSpace(time) ||
                string.IsNullOrWhiteSpace(timezone))
            {
                return null;
            }

            if (!TimeSpan.TryParse(time, out var parsedTime))
                return null;

            try
            {
                var sourceTz = TimeZoneInfo.FindSystemTimeZoneById(timezone);

                var sourceDateTime = DateTime.SpecifyKind(
                    DateTime.Today.Add(parsedTime),
                    DateTimeKind.Unspecified);

                var localDateTime = TimeZoneInfo.ConvertTime(
                    sourceDateTime,
                    sourceTz,
                    TimeZoneInfo.Local);

                return localDateTime.ToString("HH:mm");
            }
            catch
            {
                return time;
            }
        }
    }
}
