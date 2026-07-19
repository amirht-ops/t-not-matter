using System.Net.Http.Json;
using AuthorizationService.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace AuthorizationService.Infrastructure.OpaClient;

public sealed class OpaDataUpdater(
    HttpClient httpClient,
    IOptions<OpaOptions> options) : IOpaDataUpdater
{
    private readonly OpaOptions _options = options.Value;

    public async Task SyncPolicyDataAsync(string documentPath, object data, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(_options.TimeoutMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var url = $"/v1/data/{documentPath}";
        using var response = await httpClient.PutAsJsonAsync(url, data, linked.Token);
        response.EnsureSuccessStatusCode();
    }
}
