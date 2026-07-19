using System.Net;
using AuthorizationService.Application.Common.Abstractions;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace AuthorizationService.Infrastructure.Services;

public sealed class TenantServiceClient(HttpClient httpClient) : ITenantServiceClient
{
    public async Task<Result<bool>> ValidateDepartmentExistsAsync(Guid tenantId, Guid departmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"/api/v1/tenants/{tenantId}/departments/{departmentId}");
            var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
                return Result<bool>.Success(true);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result<bool>.Success(false);

            return Result<bool>.Failure(GeneralErrors.ServiceUnavailable);
        }
        catch (HttpRequestException) { return Result<bool>.Failure(GeneralErrors.ServiceUnavailable); }
        catch (OperationCanceledException) { return Result<bool>.Failure(GeneralErrors.ServiceUnavailable); }
    }
}