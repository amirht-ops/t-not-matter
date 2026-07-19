using System.Collections.Generic;
using IdentityService.Domain.Aggregates.Session;
using IdentityService.Domain.Aggregates.User;

namespace IdentityService.Domain.Services;

public sealed record GeneratedToken(string AccessToken, DateTimeOffset ExpiresAt);

public interface ITokenGenerator
{
    GeneratedToken GenerateAccessToken(User user, Session session, string tenantSlug, Guid correlationId, Guid? departmentId = null, Guid? roleId = null, IReadOnlyList<string>? roles = null);
}
