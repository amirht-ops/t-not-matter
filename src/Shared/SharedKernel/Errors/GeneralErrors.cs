namespace SharedKernel.Errors;

public static class GeneralErrors
{
    public static readonly Error Validation =
        Error.Validation("General.Validation", "The request is invalid.");

    public static readonly Error NotFound =
        Error.NotFound("General.NotFound", "The requested resource was not found.");

    public static readonly Error Conflict =
        Error.Conflict("General.Conflict", "The resource already exists or is in conflict.");

    public static readonly Error Unauthorized =
        Error.Unauthorized("General.Unauthorized", "Authentication failed.");

    public static readonly Error Forbidden =
        Error.Forbidden("General.Forbidden", "Access denied.");

    public static readonly Error TenantMissing =
        Error.Validation("Tenant.Missing", "TenantId is required for all operations.");

    public static readonly Error ServiceUnavailable =
        Error.Failure("General.ServiceUnavailable", "The service is temporarily unavailable.");
}
