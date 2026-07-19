using System.Reflection;
using AuthorizationService.Domain.Aggregates.Permission;
using AuthorizationService.Domain.Aggregates.PermissionGrant;
using AuthorizationService.Domain.Aggregates.Role;
using AuthorizationService.Domain.Aggregates.RoleAssignment;
using AuthorizationService.Domain.Aggregates.UsageTracking;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AuthorizationService.Infrastructure.Persistence.Seed;

public static class AuthorizationDataSeeder
{
    // Admin tenant + department
    public static readonly Guid AdminTenantId = Guid.Parse("5f9c9b5a-d1b4-4b53-8d60-70f90e5f0ea5");
    public static readonly Guid AdminDepartmentId = Guid.Parse("2a4b5c6d-7e8f-9a0b-1c2d-3e4f5a6b7c8d");
    public static readonly Guid SuperUserId = Guid.Parse("8f9c9b5a-d1b4-4b53-8d60-70f90e5f0ea6");
    public static readonly Guid SuperAdminRoleId = Guid.Parse("c2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d03");

    // Acme tenant + users
    public static readonly Guid TenantId = Guid.Parse("6f9c9b5a-d1b4-4b53-8d60-70f90e5f0ea5");
    public static readonly Guid DevelopmentDepartmentId = Guid.Parse("3a4b5c6d-7e8f-9a0b-1c2d-3e4f5a6b7c8d");
    public static readonly Guid SupportDepartmentId = Guid.Parse("1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d");
    public static readonly Guid SeniorDevUserId = Guid.Parse("a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d");
    public static readonly Guid SupportLeadUserId = Guid.Parse("f5e4d3c2-b1a0-9f8e-7d6c-5b4a3f2e1d0c");

    // Permissions
    private static readonly Guid PermissionInvoiceCreateId = Guid.Parse("a1b2c3d4-e5f6-4a8b-9c0d-1e2f3a4b5c01");
    private static readonly Guid PermissionInvoiceReadId = Guid.Parse("a1b2c3d4-e5f6-4a8b-9c0d-1e2f3a4b5c02");
    private static readonly Guid PermissionInvoiceDeleteId = Guid.Parse("a1b2c3d4-e5f6-4a8b-9c0d-1e2f3a4b5c03");
    private static readonly Guid PermissionPlatformManageId = Guid.Parse("a1b2c3d4-e5f6-4a8b-9c0d-1e2f3a4b5c04");

    // Roles
    private static readonly Guid DeveloperRoleId = Guid.Parse("b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d01");
    private static readonly Guid ManagerRoleId = Guid.Parse("b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d02");

    public static async Task SeedAsync(AuthorizationDbContext dbContext, ILogger logger)
    {
        var seeded = false;

        seeded |= await SeedPermissionsAsync(dbContext, logger);
        seeded |= await SeedRolesAsync(dbContext, logger);
        seeded |= await SeedPermissionGrantsAsync(dbContext, logger);
        seeded |= await SeedRoleAssignmentsAsync(dbContext, logger);
        seeded |= await SeedUsageTrackingAsync(dbContext, logger);

        if (seeded)
        {
            await dbContext.SaveChangesAsync();
            logger.LogInformation("Authorization seed data committed successfully");
        }
    }

    private static async Task<bool> SeedPermissionsAsync(AuthorizationDbContext dbContext, ILogger logger)
    {
        var seeded = false;

        if (!await dbContext.Permissions.IgnoreQueryFilters().AnyAsync(p => p.Id == PermissionInvoiceCreateId))
        {
            var key = PermissionKey.FromDb("invoice.create");
            var action = AuthorizationAction.Create("create");
            var permission = CreatePermission(PermissionInvoiceCreateId, TenantId, key, action,
                "invoice", "Allows creating invoices", "System", PermissionLifecycle.Published);
            dbContext.Permissions.Add(permission);
            seeded = true;
            logger.LogInformation("Seeded permission: invoice.create");
        }

        if (!await dbContext.Permissions.IgnoreQueryFilters().AnyAsync(p => p.Id == PermissionInvoiceReadId))
        {
            var key = PermissionKey.FromDb("invoice.read");
            var action = AuthorizationAction.Create("read");
            var permission = CreatePermission(PermissionInvoiceReadId, TenantId, key, action,
                "invoice", "Allows reading invoices", "System", PermissionLifecycle.Published);
            dbContext.Permissions.Add(permission);
            seeded = true;
            logger.LogInformation("Seeded permission: invoice.read");
        }

        if (!await dbContext.Permissions.IgnoreQueryFilters().AnyAsync(p => p.Id == PermissionInvoiceDeleteId))
        {
            var key = PermissionKey.FromDb("invoice.delete");
            var action = AuthorizationAction.Create("delete");
            var permission = CreatePermission(PermissionInvoiceDeleteId, TenantId, key, action,
                "invoice", "Allows deleting invoices", "System", PermissionLifecycle.Published);
            dbContext.Permissions.Add(permission);
            seeded = true;
            logger.LogInformation("Seeded permission: invoice.delete");
        }

        if (!await dbContext.Permissions.IgnoreQueryFilters().AnyAsync(p => p.Id == PermissionPlatformManageId))
        {
            var key = PermissionKey.FromDb("platform.manage");
            var action = AuthorizationAction.Create("platform.manage");
            var permission = CreatePermission(PermissionPlatformManageId, AdminTenantId, key, action,
                "platform", "Grants platform-level administration rights", "System", PermissionLifecycle.Published);
            dbContext.Permissions.Add(permission);
            seeded = true;
            logger.LogInformation("Seeded permission: platform.manage");
        }

        return seeded;
    }

    private static async Task<bool> SeedRolesAsync(AuthorizationDbContext dbContext, ILogger logger)
    {
        var seeded = false;

        if (!await dbContext.Roles.IgnoreQueryFilters().AnyAsync(r => r.Id == SuperAdminRoleId))
        {
            var name = RoleName.Create("SuperAdministrator");
            var role = CreateRole(SuperAdminRoleId, AdminTenantId, AdminDepartmentId, name,
                "Unrestricted platform administration", null, 0);
            dbContext.Roles.Add(role);
            seeded = true;
            logger.LogInformation("Seeded role: SuperAdministrator");
        }

        if (!await dbContext.Roles.IgnoreQueryFilters().AnyAsync(r => r.Id == DeveloperRoleId))
        {
            var name = RoleName.Create("DeveloperRole");
            var role = CreateRole(DeveloperRoleId, TenantId, DevelopmentDepartmentId, name,
                "Developer role with read access", null, 0);
            dbContext.Roles.Add(role);
            seeded = true;
            logger.LogInformation("Seeded role: DeveloperRole");
        }

        if (!await dbContext.Roles.IgnoreQueryFilters().AnyAsync(r => r.Id == ManagerRoleId))
        {
            var name = RoleName.Create("ManagerRole");
            var role = CreateRole(ManagerRoleId, TenantId, SupportDepartmentId, name,
                "Manager role with full invoice access", null, 0);
            dbContext.Roles.Add(role);
            seeded = true;
            logger.LogInformation("Seeded role: ManagerRole");
        }

        return seeded;
    }

    private static async Task<bool> SeedPermissionGrantsAsync(AuthorizationDbContext dbContext, ILogger logger)
    {
        var seeded = false;

        var existingGrants = await dbContext.PermissionGrants.IgnoreQueryFilters().ToListAsync();
        var grantedBy = UserId.From(SuperUserId);

        // SuperAdministrator gets ALL published permissions
        var publishedPermissions = await dbContext.Permissions
            .IgnoreQueryFilters()
            .Where(p => p.Lifecycle == PermissionLifecycle.Published)
            .ToListAsync();

        foreach (var permission in publishedPermissions)
        {
            if (existingGrants.All(g => g.RoleId.Value != SuperAdminRoleId || g.PermissionId.Value != permission.Id))
            {
                var grant = PermissionGrant.Grant(AdminTenantId, RoleId.From(SuperAdminRoleId),
                    PermissionId.From(permission.Id), grantedBy, Guid.NewGuid());
                dbContext.PermissionGrants.Add(grant);
                seeded = true;
                logger.LogInformation("Seeded permission grant: SuperAdministrator -> {PermissionKey}", permission.Key);
            }
        }

        // DeveloperRole gets invoice.read only
        if (existingGrants.All(g => g.RoleId.Value != DeveloperRoleId || g.PermissionId.Value != PermissionInvoiceReadId))
        {
            var grant = PermissionGrant.Grant(TenantId, RoleId.From(DeveloperRoleId),
                PermissionId.From(PermissionInvoiceReadId), grantedBy, Guid.NewGuid());
            dbContext.PermissionGrants.Add(grant);
            seeded = true;
            logger.LogInformation("Seeded permission grant: DeveloperRole -> invoice.read");
        }

        // ManagerRole gets invoice.create, invoice.read, invoice.delete
        if (existingGrants.All(g => g.RoleId.Value != ManagerRoleId || g.PermissionId.Value != PermissionInvoiceCreateId))
        {
            var grant = PermissionGrant.Grant(TenantId, RoleId.From(ManagerRoleId),
                PermissionId.From(PermissionInvoiceCreateId), grantedBy, Guid.NewGuid());
            dbContext.PermissionGrants.Add(grant);
            seeded = true;
            logger.LogInformation("Seeded permission grant: ManagerRole -> invoice.create");
        }

        if (existingGrants.All(g => g.RoleId.Value != ManagerRoleId || g.PermissionId.Value != PermissionInvoiceReadId))
        {
            var grant = PermissionGrant.Grant(TenantId, RoleId.From(ManagerRoleId),
                PermissionId.From(PermissionInvoiceReadId), grantedBy, Guid.NewGuid());
            dbContext.PermissionGrants.Add(grant);
            seeded = true;
            logger.LogInformation("Seeded permission grant: ManagerRole -> invoice.read");
        }

        if (existingGrants.All(g => g.RoleId.Value != ManagerRoleId || g.PermissionId.Value != PermissionInvoiceDeleteId))
        {
            var grant = PermissionGrant.Grant(TenantId, RoleId.From(ManagerRoleId),
                PermissionId.From(PermissionInvoiceDeleteId), grantedBy, Guid.NewGuid());
            dbContext.PermissionGrants.Add(grant);
            seeded = true;
            logger.LogInformation("Seeded permission grant: ManagerRole -> invoice.delete");
        }

        return seeded;
    }

    private static async Task<bool> SeedRoleAssignmentsAsync(AuthorizationDbContext dbContext, ILogger logger)
    {
        var seeded = false;

        var existingAssignments = await dbContext.RoleAssignments.IgnoreQueryFilters().ToListAsync();
        var assignedBy = UserId.From(SuperUserId);

        if (existingAssignments.All(a => a.SubjectId.Value != SuperUserId || a.RoleId.Value != SuperAdminRoleId))
        {
            var assignment = RoleAssignment.Assign(AdminTenantId, SubjectId.From(SuperUserId),
                RoleId.From(SuperAdminRoleId), AdminDepartmentId, assignedBy, Guid.NewGuid());
            dbContext.RoleAssignments.Add(assignment);
            seeded = true;
            logger.LogInformation("Seeded role assignment: super -> SuperAdministrator");
        }

        if (existingAssignments.All(a => a.SubjectId.Value != SeniorDevUserId || a.RoleId.Value != DeveloperRoleId))
        {
            var assignment = RoleAssignment.Assign(TenantId, SubjectId.From(SeniorDevUserId),
                RoleId.From(DeveloperRoleId), DevelopmentDepartmentId, assignedBy, Guid.NewGuid());
            dbContext.RoleAssignments.Add(assignment);
            seeded = true;
            logger.LogInformation("Seeded role assignment: dev -> DeveloperRole");
        }

        if (existingAssignments.All(a => a.SubjectId.Value != SupportLeadUserId || a.RoleId.Value != ManagerRoleId))
        {
            var assignment = RoleAssignment.Assign(TenantId, SubjectId.From(SupportLeadUserId),
                RoleId.From(ManagerRoleId), SupportDepartmentId, assignedBy, Guid.NewGuid());
            dbContext.RoleAssignments.Add(assignment);
            seeded = true;
            logger.LogInformation("Seeded role assignment: lead -> ManagerRole");
        }

        return seeded;
    }

    private static async Task<bool> SeedUsageTrackingAsync(AuthorizationDbContext dbContext, ILogger logger)
    {
        var seeded = false;

        // Generous wildcard quotas for super (no limits during testing)
        if (!await dbContext.UsageTrackings.IgnoreQueryFilters()
                .AnyAsync(u => u.SubjectId == SubjectId.From(SuperUserId) && u.Action == "*"))
        {
            var now = DateTimeOffset.UtcNow;

            dbContext.UsageTrackings.Add(UsageTracking.Create(
                AdminTenantId, SubjectId.From(SuperUserId), "*", "*", "*",
                TimeWindow.Daily(now), Guid.NewGuid()));

            dbContext.UsageTrackings.Add(UsageTracking.Create(
                AdminTenantId, SubjectId.From(SuperUserId), "*", "*", "*",
                TimeWindow.Weekly(now), Guid.NewGuid()));

            dbContext.UsageTrackings.Add(UsageTracking.Create(
                AdminTenantId, SubjectId.From(SuperUserId), "*", "*", "*",
                TimeWindow.Monthly(now), Guid.NewGuid()));

            seeded = true;
            logger.LogInformation("Seeded usage tracking: super (wildcard daily/weekly/monthly)");
        }

        // Dev gets daily invoice.read tracking
        if (!await dbContext.UsageTrackings.IgnoreQueryFilters()
                .AnyAsync(u => u.SubjectId == SubjectId.From(SeniorDevUserId) && u.Action == "invoice.read"))
        {
            var window = TimeWindow.Daily(DateTimeOffset.UtcNow);
            dbContext.UsageTrackings.Add(UsageTracking.Create(
                TenantId, SubjectId.From(SeniorDevUserId), "invoice.read", "invoice", "*",
                window, Guid.NewGuid()));
            seeded = true;
            logger.LogInformation("Seeded usage tracking: dev -> invoice.read (daily)");
        }

        return seeded;
    }

    private static Permission CreatePermission(Guid id, Guid tenantId, PermissionKey key,
        AuthorizationAction action, string resourceType, string? description, string ownerTeam,
        PermissionLifecycle lifecycle)
    {
        var constructor = typeof(Permission).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c =>
            {
                var p = c.GetParameters();
                return p.Length == 7
                    && p[0].ParameterType == typeof(Guid)
                    && p[1].ParameterType == typeof(Guid)
                    && p[2].ParameterType == typeof(PermissionKey);
            });

        var permission = (Permission)constructor.Invoke([id, tenantId, key, action, resourceType, description, ownerTeam]);

        var lifecycleField = typeof(Permission).GetField("<Lifecycle>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance);
        lifecycleField?.SetValue(permission, lifecycle);

        if (lifecycle == PermissionLifecycle.Published)
        {
            var publishedAtField = typeof(Permission).GetField("<PublishedAt>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            publishedAtField?.SetValue(permission, DateTimeOffset.UtcNow);
        }

        return permission;
    }

    private static Role CreateRole(Guid id, Guid tenantId, Guid departmentId, RoleName name,
        string? description, RoleId? parentRoleId, int hierarchyDepth)
    {
        var constructor = typeof(Role).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c =>
            {
                var p = c.GetParameters();
                return p.Length == 7
                    && p[0].ParameterType == typeof(Guid)
                    && p[1].ParameterType == typeof(Guid)
                    && p[2].ParameterType == typeof(Guid)
                    && p[3].ParameterType == typeof(RoleName);
            });

        return (Role)constructor.Invoke([id, tenantId, departmentId, name, description, parentRoleId, hierarchyDepth]);
    }
}
