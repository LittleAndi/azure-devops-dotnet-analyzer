namespace Infrastructure;
public class ApiVersionHandler : DelegatingHandler
{
    private readonly string _apiVersion = "7.2-preview";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri == null) throw new ArgumentNullException("request.RequestUri");
        var uriBuilder = new UriBuilder(request.RequestUri);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);
        query["api-version"] = _apiVersion;
        uriBuilder.Query = query.ToString();
        request.RequestUri = uriBuilder.Uri;

        return await base.SendAsync(request, cancellationToken);
    }
}