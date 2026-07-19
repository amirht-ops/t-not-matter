using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Abstractions.Infrastructure;

namespace AuthorizationService.Infrastructure.OpaClient;

/// <summary>
/// Provisions the base authorization policy (Rego) into OPA at startup.
///
/// The decision engine queries <c>data.authorization.allow</c> (see
/// <see cref="OpaOptions.PolicyPath"/>). OPA runs in-memory in the dev/test
/// topology, so the policy rule must be (re)loaded whenever OPA (re)starts —
/// otherwise every OPA-backed decision fails closed to Deny. The RBAC/permission
/// <em>data</em> is synchronized separately by <c>OpaSyncConsumer</c>; this task
/// owns the <em>policy rule</em> itself.
/// </summary>
public sealed class OpaPolicyProvisioningTask : IStartupTask
{
    // OPA policy id (path segment under /v1/policies/{id}); distinct from the
    // package name inside the module.
    private const string PolicyId = "authorization";
    private const string PolicyResourceName =
        "AuthorizationService.Infrastructure.OpaClient.Policies.authorization.rego";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<OpaOptions> _options;
    private readonly ILogger<OpaPolicyProvisioningTask> _logger;

    public OpaPolicyProvisioningTask(
        IHttpClientFactory httpClientFactory,
        IOptions<OpaOptions> options,
        ILogger<OpaPolicyProvisioningTask> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public string DisplayName => "OPA Policy Provisioning";

    // Run after the OPA HttpClient warmup (Order 0) so the socket is primed.
    public int Order => 10;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var rego = await LoadPolicySourceAsync();
        if (rego is null)
        {
            _logger.LogError(
                "OPA base policy resource '{Resource}' not found; OPA-backed decisions will fail closed.",
                PolicyResourceName);
            return;
        }

        var baseUrl = _options.Value.BaseUrl.TrimEnd('/');
        using var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(10);

        using var content = new StringContent(rego);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        try
        {
            using var response = await client.PutAsync($"/v1/policies/{PolicyId}", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Provisioned OPA base authorization policy '{PolicyId}'.", PolicyId);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Failed to provision OPA base policy '{PolicyId}': {StatusCode} {Body}",
                    PolicyId, (int)response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: log loudly. OPA warmup already tolerates a not-yet-ready
            // OPA; a failed provision leaves decisions failing closed (safe).
            _logger.LogError(ex, "Error provisioning OPA base policy '{PolicyId}'.", PolicyId);
        }
    }

    private static async Task<string?> LoadPolicySourceAsync()
    {
        var assembly = typeof(OpaPolicyProvisioningTask).Assembly;
        await using var stream = assembly.GetManifestResourceStream(PolicyResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
