using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Abstractions.Infrastructure;

namespace Platform.Infrastructure.Startup;

public sealed class HttpClientWarmupTask : IStartupTask
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<HttpClientWarmupOptions> _options;
    private readonly ILogger<HttpClientWarmupTask> _logger;

    public HttpClientWarmupTask(
        IHttpClientFactory httpClientFactory,
        IOptions<HttpClientWarmupOptions> options,
        ILogger<HttpClientWarmupTask> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public string DisplayName => "HttpClient Warmup";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var urls = _options.Value.BaseUrls;
        if (urls.Count == 0)
        {
            _logger.LogInformation("No downstream URLs configured for HttpClient warmup");
            return;
        }

        _logger.LogInformation("Warming up {Count} downstream HttpClient connection(s)", urls.Count);

        var warmupTasks = urls.Select(url => WarmupUrlAsync(url, cancellationToken));
        await Task.WhenAll(warmupTasks);

        _logger.LogInformation("HttpClient warmup completed");
    }

    private async Task WarmupUrlAsync(string baseUrl, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Warming HttpClient connection pool for {BaseUrl}", baseUrl);

            using var client = _httpClientFactory.CreateClient();
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(3);

            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl);
            using var response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            _logger.LogDebug("HttpClient warmup for {BaseUrl} returned {StatusCode}",
                baseUrl, (int)response.StatusCode);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("HttpClient warmup for {BaseUrl} timed out (expected)", baseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HttpClient warmup for {BaseUrl} failed (expected if downstream not ready)", baseUrl);
        }
    }
}

public sealed class HttpClientWarmupOptions
{
    public const string SectionName = "HttpClientWarmup";

    public List<string> BaseUrls { get; set; } = new();
}
