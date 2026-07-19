using SharedKernel.Errors;

namespace TenantService.Domain.Errors;

public static class TenantErrors
{
    public static readonly Error InvalidIdentifierFormat =
        Error.Validation("Tenant.InvalidIdentifierFormat", "Tenant identifier must match format 'tnnt_[a-z0-9]{22}'.");

    public static readonly Error IdentifierAlreadyExists =
        Error.Conflict("Tenant.IdentifierAlreadyExists", "A tenant with this identifier already exists.");

    public static readonly Error TenantNotFound =
        Error.NotFound("Tenant.NotFound", "The requested tenant was not found.");

    public static readonly Error TenantIsDisabled =
        Error.Forbidden("Tenant.IsDisabled", "The tenant is disabled and cannot perform this operation.");

    public static readonly Error TenantIsSuspended =
        Error.Forbidden("Tenant.IsSuspended", "The tenant is suspended and cannot perform this operation.");

    public static readonly Error InvalidStatusTransition =
        Error.Conflict("Tenant.InvalidStatusTransition", "Invalid tenant status transition.");

    public static readonly Error CannotUpgradeFromDisabled =
        Error.Conflict("Tenant.CannotUpgradeFromDisabled", "Cannot upgrade plan for a disabled tenant.");

    public static readonly Error CannotUpgradeFromSuspended =
        Error.Conflict("Tenant.CannotUpgradeFromSuspended", "Cannot upgrade plan for a suspended tenant.");

    public static readonly Error InvalidIdentifier =
        Error.Validation("Tenant.InvalidIdentifier",
            "Tenant identifier must be 3-50 characters, lowercase alphanumeric with hyphens, starting with a letter.");

    public static readonly Error NameRequired =
        Error.Validation("Tenant.NameRequired", "Tenant name is required.");

    public static readonly Error NameTooLong =
        Error.Validation("Tenant.NameTooLong", "Tenant name cannot exceed 200 characters.");

    public static readonly Error SamePlanTier =
        Error.Validation("Tenant.SamePlanTier", "Tenant is already on the requested plan tier.");

    public static readonly Error DepartmentNotFound =
        Error.NotFound("Tenant.DepartmentNotFound", "The requested department was not found.");

    public static readonly Error DepartmentAlreadyExists =
        Error.Conflict("Tenant.DepartmentAlreadyExists", "A department with this name already exists in this tenant.");

    public static readonly Error DepartmentNameRequired =
        Error.Validation("Tenant.DepartmentNameRequired", "Department name is required.");

    public static readonly Error DepartmentNameTooLong =
        Error.Validation("Tenant.DepartmentNameTooLong", "Department name cannot exceed 200 characters.");

    public static readonly Error DepartmentIsInactive =
        Error.Conflict("Tenant.DepartmentIsInactive", "The department is inactive.");

    public static readonly Error SlugRequired =
        Error.Validation("Tenant.SlugRequired", "Tenant slug is required.");

    public static readonly Error SlugTooShort =
        Error.Validation("Tenant.SlugTooShort", "Tenant slug must be at least 2 characters.");

    public static readonly Error SlugTooLong =
        Error.Validation("Tenant.SlugTooLong", "Tenant slug cannot exceed 63 characters.");

    public static readonly Error SlugInvalidFormat =
        Error.Validation("Tenant.SlugInvalidFormat",
            "Tenant slug must be lowercase alphanumeric with hyphens, starting and ending with a letter or digit.");

    public static readonly Error SlugAlreadyExists =
        Error.Conflict("Tenant.SlugAlreadyExists", "A tenant with this slug already exists.");

    public static readonly Error SlugIsReserved =
        Error.Validation("Tenant.SlugIsReserved", "The specified slug is reserved and cannot be used.");

    public static readonly Error LockServiceUnavailable =
        Error.Failure("Tenant.LockServiceUnavailable",
            "The distributed lock service is temporarily unavailable. Please retry the request.");
}