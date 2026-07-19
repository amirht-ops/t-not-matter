using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace AuthorizationService.Infrastructure.OpaClient;

public sealed class OpaHttpClient(HttpClient httpClient, IOptions<OpaOptions> options)
{
    private readonly OpaOptions options = options.Value;

    public async Task<OpaResponse?> EvaluateAsync(OpaRequest request, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(options.TimeoutMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        using var response = await httpClient.PostAsJsonAsync(options.PolicyPath, request, linked.Token);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<OpaResponse>(cancellationToken: linked.Token);
    }
}
