using SharedKernel.Errors;

namespace IdentityService.Domain.Errors;

public static class IdentityErrors
{
    public static readonly Error EmailRequired =
        Error.Validation("Identity.EmailRequired", "Email is required.");

    public static readonly Error EmailInvalid =
        Error.Validation("Identity.EmailInvalid", "Email format is invalid.");

    public static readonly Error PasswordHashRequired =
        Error.Validation("Identity.PasswordHashRequired", "Password hash is required.");

    public static readonly Error MfaSecretRequired =
        Error.Validation("Identity.MfaSecretRequired", "Protected MFA secret is required.");

    public static readonly Error InvalidStatusTransition =
        Error.Conflict("Identity.InvalidStatusTransition", "Invalid user lifecycle transition.");

    public static readonly Error UserMustBeActive =
        Error.Conflict("Identity.UserMustBeActive", "User must be Active to perform this operation.");

    public static readonly Error UserNotFound =
        Error.NotFound("Identity.UserNotFound", "The requested user was not found.");

    public static readonly Error SessionNotFound =
        Error.NotFound("Identity.SessionNotFound", "The requested session was not found.");

    public static readonly Error UsernameRequired =
        Error.Validation("Identity.UsernameRequired", "Username is required.");

    public static readonly Error InvalidUsername =
        Error.Validation("Identity.InvalidUsername", "Username is invalid.");

    public static readonly Error PhoneNumberRequired =
        Error.Validation("Identity.PhoneNumberRequired", "Phone number is required.");

    public static readonly Error PhoneNumberInvalid =
        Error.Validation("Identity.PhoneNumberInvalid", "Phone number must be in E.164 format (e.g., +1234567890).");

    public static readonly Error CurrentPasswordInvalid =
        Error.Validation("Identity.CurrentPasswordInvalid", "Current password is incorrect.");

    public static readonly Error NewPasswordSameAsCurrent =
        Error.Validation("Identity.NewPasswordSameAsCurrent", "New password must be different from current password.");

    public static readonly Error MfaAlreadyEnabled =
        Error.Validation("Identity.MfaAlreadyEnabled", "MFA is already enabled.");

    public static readonly Error MfaNotEnabled =
        Error.Validation("Identity.MfaNotEnabled", "MFA is not enabled.");

    public static readonly Error UserLocked =
        Error.Failure("Identity.UserLocked", "User account is locked due to too many failed login attempts.");

    public static readonly Error LoginIdentifierRequired =
        Error.Validation("Identity.LoginIdentifierRequired", "Login identifier is required.");

    public static readonly Error LoginIdentifierInvalidFormat =
        Error.Validation("Identity.LoginIdentifierInvalidFormat", "Login identifier must be in the format 'slug.email@domain.com'.");

    public static readonly Error SlugRequired =
        Error.Validation("Identity.SlugRequired", "Tenant slug is required.");

    public static readonly Error SlugTooShort =
        Error.Validation("Identity.SlugTooShort", "Tenant slug must be at least 2 characters.");

    public static readonly Error SlugTooLong =
        Error.Validation("Identity.SlugTooLong", "Tenant slug cannot exceed 63 characters.");

    public static readonly Error SlugInvalidFormat =
        Error.Validation("Identity.SlugInvalidFormat", "Tenant slug must be lowercase alphanumeric with hyphens, starting and ending with a letter or digit.");

    public static readonly Error SlugResolutionFailed =
        Error.Failure("Identity.SlugResolutionFailed", "Failed to resolve tenant from slug. The service is temporarily unavailable.");

    public static readonly Error SlugNotFound =
        Error.NotFound("Identity.SlugNotFound", "No tenant found with the specified slug.");

    public static readonly Error TenantServiceUnauthorized =
        Error.Unauthorized("Identity.TenantServiceUnauthorized", "Service-to-service authentication failed.");

    public static readonly Error TenantServiceUnavailable =
        Error.Failure("Identity.TenantServiceUnavailable", "Tenant service is temporarily unavailable.");

    public static readonly Error TenantServiceTimeout =
        Error.Failure("Identity.TenantServiceTimeout", "Tenant service request timed out.");
}
