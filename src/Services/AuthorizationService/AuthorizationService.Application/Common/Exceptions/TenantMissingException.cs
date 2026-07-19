namespace AuthorizationService.Application.Common.Exceptions;

public sealed class TenantMissingException() : AuthorizationException("TenantId is required.");
