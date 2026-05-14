using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem
{
    public class JikanHeaderHandler : DelegatingHandler
    {
        public JikanHeaderHandler(HttpMessageHandler inner)
            : base(inner)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            if (response is not null)
            {
                var fullUrl = request.RequestUri.ToString();

                DateTime? expiry = null;

                var expiresHeader = response.Content?.Headers.Expires;

                if (expiresHeader.HasValue)
                {
                    expiry = expiresHeader.Value.UtcDateTime;
                }

                if (expiry.HasValue)
                {
                    JikanHttpMetadataStore.SetExpiry(fullUrl, expiry.Value);
                }
            }

            return response;
        }
    }
}
