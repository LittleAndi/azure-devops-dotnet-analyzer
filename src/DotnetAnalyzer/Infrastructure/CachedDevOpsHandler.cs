using System.Net;
using Microsoft.Extensions.Caching.Memory;

namespace Infrastructure;

public class CachedDevOpsHandler(IMemoryCache cache) : DelegatingHandler
{
    private readonly IMemoryCache cache = cache;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = request!.RequestUri!.ToString();
        var cached = cache.Get<string>(key);
        if (cached is not null)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(cached)
            };
        }

        var response = await base.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        cache.Set(key, content, TimeSpan.FromMinutes(5));
        return response;
    }
}