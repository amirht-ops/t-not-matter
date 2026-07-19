using SharedKernel.Errors;

namespace AuthorizationService.Domain.Errors;

public static class AuthorizationErrors
{
    // Role errors
    public static readonly Error RoleNameRequired =
        Error.Validation("Authorization.RoleNameRequired", "Role name is required.");

    public static readonly Error MaxRolesPerTenantExceeded =
        Error.Conflict("Authorization.MaxRolesPerTenantExceeded", "Maximum roles per tenant exceeded.");

    public static readonly Error MaxHierarchyDepthExceeded =
        Error.Validation("Authorization.MaxHierarchyDepthExceeded", "Maximum role hierarchy depth exceeded.");

    public static readonly Error RoleSelfInheritance =
        Error.Validation("Authorization.RoleSelfInheritance", "Role cannot inherit from itself.");

    public static readonly Error RoleTenantMismatch =
        Error.Failure("Authorization.RoleTenantMismatch", "Role tenant mismatch.");

    public static readonly Error RoleNotFound =
        Error.NotFound("Authorization.RoleNotFound", "The requested role was not found.");

    public static readonly Error RoleNotActive =
        Error.Failure("Authorization.RoleNotActive", "Role is not active.");

    public static readonly Error RoleIsDisabled =
        Error.Conflict("Authorization.RoleIsDisabled", "Role is disabled and cannot be modified.");

    // Permission lifecycle errors
    public static readonly Error InvalidPermissionLifecycle =
        Error.Validation("Authorization.InvalidPermissionLifecycle", "Invalid permission lifecycle transition.");

    public static readonly Error PermissionNotPublished =
        Error.Failure("Authorization.PermissionNotPublished", "Only Published permissions can be granted.");

    public static readonly Error PermissionTenantMismatch =
        Error.Failure("Authorization.PermissionTenantMismatch", "Permission tenant mismatch.");

    public static readonly Error InvalidVersionSource =
        Error.Validation("Authorization.InvalidVersionSource", "New versions can only be created from published or deprecated permissions.");

    // Permission errors
    public static readonly Error PermissionNotFound =
        Error.NotFound("Authorization.PermissionNotFound", "The requested permission was not found.");

    public static readonly Error PermissionKeyInvalidFormat =
        Error.Validation("Authorization.PermissionKeyInvalidFormat", "Permission key format is invalid.");

    public static readonly Error GlobalPermissionProhibited =
        Error.Validation("Authorization.GlobalPermissionProhibited", "Global permissions are prohibited.");

    // Usage tracking errors
    public static readonly Error CannotIncrementExpiredUsage =
        Error.Failure("Authorization.CannotIncrementExpiredUsage", "Cannot increment expired usage tracking.");

    public static readonly Error UsageTrackingTenantMismatch =
        Error.Failure("Authorization.UsageTrackingTenantMismatch", "Usage tracking tenant mismatch.");

    // Usage counter errors
    public static readonly Error NegativeCount =
        Error.Validation("Authorization.NegativeCount", "Usage count cannot be negative.");

    public static readonly Error InvalidWindowRange =
        Error.Validation("Authorization.InvalidWindowRange", "WindowStart must be before WindowEnd.");

    // Assignment errors
    public static readonly Error AssignmentTenantMismatch =
        Error.Failure("Authorization.AssignmentTenantMismatch", "RoleAssignment tenant mismatch.");

    public static readonly Error SubjectAlreadyHasRole =
        Error.Conflict("Authorization.SubjectAlreadyHasRole", "Subject already has an active role. Revoke the existing role first.");

    public static readonly Error SubjectHasNoActiveRoleAssignment =
        Error.NotFound("Authorization.SubjectHasNoActiveRoleAssignment", "Subject has no active role assignment.");

    public static readonly Error RoleDepartmentMismatch =
        Error.Conflict("Authorization.RoleDepartmentMismatch", "Target role belongs to a different department.");

    public static readonly Error SubjectDepartmentMismatch =
        Error.Conflict("Authorization.SubjectDepartmentMismatch", "Subject does not belong to the target role's department.");

    // Grant errors
    public static readonly Error GrantTenantMismatch =
        Error.Failure("Authorization.GrantTenantMismatch", "PermissionGrant tenant mismatch.");
}
