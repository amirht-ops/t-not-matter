namespace SharedKernel.Errors;

public static class TenantSlugErrors
{
    public static readonly Error Required =
        Error.Validation("TenantSlug.Required", "Tenant slug is required.");

    public static readonly Error TooShort =
        Error.Validation("TenantSlug.TooShort", "Tenant slug must be at least 2 characters.");

    public static readonly Error TooLong =
        Error.Validation("TenantSlug.TooLong", "Tenant slug cannot exceed 63 characters.");

    public static readonly Error InvalidFormat =
        Error.Validation("TenantSlug.InvalidFormat", "Tenant slug must be lowercase alphanumeric with hyphens, starting and ending with a letter or digit.");

    public static readonly Error IsReserved =
        Error.Validation("TenantSlug.IsReserved", "The specified slug is reserved and cannot be used.");
}
