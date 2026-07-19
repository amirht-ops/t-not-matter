using System.Net.Http.Json;
using IdentityService.Application.Common.Abstractions;
using SharedKernel.Errors;
using SharedKernel.Results;

namespace IdentityService.Infrastructure.Services;

public sealed class AuthorizationRoleResolver(HttpClient httpClient) : IAuthorizationRoleResolver
{
    public async Task<Result<ActiveRoleInfo>> GetActiveRoleForUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"/api/v1/authorization/roles/assignment?subjectId={userId}");
            request.Headers.Add("X-Tenant-Id", tenantId.ToString());

            var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ActiveRoleResponse>(cancellationToken: cancellationToken);
                if (result is not null)
                    return Result<ActiveRoleInfo>.Success(new ActiveRoleInfo(result.RoleId, result.DepartmentId));

                return Result<ActiveRoleInfo>.Success(new ActiveRoleInfo(null, null));
            }

            return Result<ActiveRoleInfo>.Failure(GeneralErrors.ServiceUnavailable);
        }
        catch (HttpRequestException)
        {
            return Result<ActiveRoleInfo>.Failure(GeneralErrors.ServiceUnavailable);
        }
        catch (Exception ex) when (ex.GetType().Name == "BrokenCircuitException")
        {
            return Result<ActiveRoleInfo>.Failure(GeneralErrors.ServiceUnavailable);
        }
        catch (OperationCanceledException)
        {
            return Result<ActiveRoleInfo>.Failure(GeneralErrors.ServiceUnavailable);
        }
    }

    private sealed record ActiveRoleResponse(Guid? RoleId, Guid? DepartmentId);
}
