using System;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.DTOs
{
    public class TimePeriodDto
    {
        public DateTime? From { get; set; }

        public DateTime? To { get; set; }

        public static TimePeriodDto Convert(JikanDotNet.TimePeriod source)
        {
            return source == null ? null : new TimePeriodDto { From = source.From?.DateTime, To = source.To?.DateTime };
        }
    }
}
