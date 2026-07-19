using System.Net;
using System.Net.Http.Json;
using IdentityService.Application.Common.Abstractions;
using IdentityService.Domain.Errors;
using SharedKernel.Errors;
using SharedKernel.Responses;
using SharedKernel.Results;

namespace IdentityService.Infrastructure.Services;

public sealed class TenantServiceClient(HttpClient httpClient) : ITenantServiceClient
{
    public async Task<Result<bool>> ValidateDepartmentExistsAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"/api/v1/tenants/{tenantId}/departments/{departmentId}");
            request.Headers.Add("X-Tenant-Id", tenantId.ToString());
            var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return Result<bool>.Success(true);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result<bool>.Success(false);

            return Result<bool>.Failure(GeneralErrors.ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            return Result<bool>.Failure(GeneralErrors.ServiceUnavailable);
        }
        catch (Exception ex) when (ex.GetType().Name == "BrokenCircuitException")
        {
            return Result<bool>.Failure(GeneralErrors.ServiceUnavailable);
        }
        catch (OperationCanceledException)
        {
            return Result<bool>.Failure(GeneralErrors.ServiceUnavailable);
        }
    }

    public async Task<Result<Guid>> ResolveTenantIdBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        try
        {
            var uri = $"/api/v1/tenants/slug/{slug}";
            var request = new HttpRequestMessage(HttpMethod.Get, uri);

            var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<TenantSlugResult>>(cancellationToken: cancellationToken);
                if (envelope?.Data is not null)
                    return Result<Guid>.Success(envelope.Data.TenantId);

                return Result<Guid>.Failure(IdentityErrors.SlugNotFound);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result<Guid>.Failure(IdentityErrors.SlugNotFound);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return Result<Guid>.Failure(IdentityErrors.TenantServiceUnauthorized);

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return Result<Guid>.Failure(GeneralErrors.Forbidden);

            if ((int)response.StatusCode >= 500)
                return Result<Guid>.Failure(IdentityErrors.TenantServiceUnavailable);

            return Result<Guid>.Failure(IdentityErrors.SlugResolutionFailed);
        }
        catch (HttpRequestException)
        {
            return Result<Guid>.Failure(IdentityErrors.TenantServiceUnavailable);
        }
        catch (Exception ex) when (ex.GetType().Name == "BrokenCircuitException")
        {
            return Result<Guid>.Failure(IdentityErrors.TenantServiceUnavailable);
        }
        catch (OperationCanceledException)
        {
            return Result<Guid>.Failure(IdentityErrors.TenantServiceTimeout);
        }
    }

    private sealed record TenantSlugResult(Guid TenantId, string Name, string Slug);
}
