using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharedKernel.Authorization;

namespace Platform.Authorization;

public sealed class AuthorizationServiceClient(
    HttpClient httpClient,
    ILogger<AuthorizationServiceClient> logger) : IAuthorizationDecisionService
{
    public async Task<AuthorizationDecision> DecideAsync(AuthorizationRequest request, CancellationToken ct)
    {
        try
        {
            var (resourceType, resourceId) = ParseResource(request.Resource);

            var evaluateRequest = new EvaluateAuthorizationRequest(
                request.SubjectId ?? Guid.Empty,
                request.Action,
                resourceType,
                resourceId,
                // OwnerId is not carried by AuthorizationRequest; the caller's tenant is
                // propagated out-of-band via the X-Tenant-Id header below.
                null,
                new Dictionary<string, string>(),
                new Dictionary<string, string>
                {
                    ["subject_status"] = "active",
                    ["tenant_status"] = "active"
                },
                new Dictionary<string, string>());

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/authorization/decisions/evaluate")
            {
                Content = JsonContent.Create(evaluateRequest)
            };

            // The decision must be evaluated in the subject's tenant. The service token used
            // for this call carries no tenant claim, so AuthorizationService would otherwise
            // resolve an empty tenant and find zero effective permissions (fail-closed deny).
            // Platform services can bypass tenant isolation, so the tenant is supplied via the
            // X-Tenant-Id override header (honored by TenantMiddleware for such principals).
            if (request.TenantId != Guid.Empty)
                httpRequest.Headers.TryAddWithoutValidation("X-Tenant-Id", request.TenantId.ToString());

            var response = await httpClient.SendAsync(httpRequest, ct);


            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Authorization service returned {StatusCode} for {Action} on {Resource}. Failing closed.",
                    (int)response.StatusCode, request.Action, request.Resource);

                return Deny("Authorization service unavailable");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var dataElement = doc.RootElement.GetProperty("data");

            if (dataElement.ValueKind == JsonValueKind.Null)
            {
                logger.LogWarning(
                    "Authorization service returned empty response for {Action} on {Resource}. Failing closed.",
                    request.Action, request.Resource);

                return Deny("Authorization service returned empty response");
            }

            var isAllowed = dataElement.GetProperty("isAllowed").GetBoolean();
            var reasonMessage = dataElement.TryGetProperty("reasonMessage", out var reasonProp)
                ? reasonProp.GetString() ?? string.Empty
                : string.Empty;

            var state = isAllowed
                ? AuthorizationDecisionState.Allow
                : AuthorizationDecisionState.Deny;

            return new AuthorizationDecision(state, reasonMessage);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Authorization service request timed out for {Action} on {Resource}. Failing closed.",
                request.Action, request.Resource);

            return Deny("Authorization service request timed out");
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex,
                "Authorization service request failed for {Action} on {Resource}. Failing closed.",
                request.Action, request.Resource);

            return Deny("Authorization service unreachable");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unexpected error during authorization for {Action} on {Resource}. Failing closed.",
                request.Action, request.Resource);

            return Deny("Authorization evaluation failed");
        }
    }

    private static (string ResourceType, string ResourceId) ParseResource(string resource)
    {
        var colonIndex = resource.IndexOf(':');
        if (colonIndex < 0)
            return (resource, string.Empty);
        return (resource[..colonIndex], resource[(colonIndex + 1)..]);
    }

    private static AuthorizationDecision Deny(string reason) =>
        new(AuthorizationDecisionState.Deny, reason);
}

public sealed record EvaluateAuthorizationRequest(
    Guid SubjectId,
    string Action,
    string ResourceType,
    string ResourceId,
    Guid? OwnerId,
    IReadOnlyDictionary<string, string> ResourceAttributes,
    IReadOnlyDictionary<string, string> EnvironmentAttributes,
    IReadOnlyDictionary<string, string> UsageAttributes);
