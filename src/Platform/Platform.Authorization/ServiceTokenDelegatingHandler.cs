using System.Net.Http.Headers;
using Platform.Abstractions.Authorization;

namespace Platform.Authorization;

public sealed class ServiceTokenDelegatingHandler(IServiceTokenGenerator tokenGenerator)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = tokenGenerator.GenerateServiceToken();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, ct);
    }
}
