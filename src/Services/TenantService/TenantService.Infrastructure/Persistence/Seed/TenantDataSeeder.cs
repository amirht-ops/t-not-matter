using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TenantService.Domain.Aggregates;
using TenantService.Domain.Enums;
using TenantService.Domain.ValueObjects;

namespace TenantService.Infrastructure.Persistence.Seed;

public static class TenantDataSeeder
{
    // Admin tenant + department
    public static readonly Guid AdminTenantId = Guid.Parse("5f9c9b5a-d1b4-4b53-8d60-70f90e5f0ea5");
    public static readonly Guid AdminDepartmentId = Guid.Parse("2a4b5c6d-7e8f-9a0b-1c2d-3e4f5a6b7c8d");

    // Acme tenant + departments
    public static readonly Guid TenantId = Guid.Parse("6f9c9b5a-d1b4-4b53-8d60-70f90e5f0ea5");
    public static readonly Guid DevelopmentDepartmentId = Guid.Parse("3a4b5c6d-7e8f-9a0b-1c2d-3e4f5a6b7c8d");
    public static readonly Guid SupportDepartmentId = Guid.Parse("1a2b3c4d-5e6f-7a8b-9c0d-1e2f3a4b5c6d");

    public static async Task SeedAsync(TenantDbContext dbContext, ILogger logger)
    {
        var seeded = false;

        seeded |= await SeedTenantsAsync(dbContext, logger);
        seeded |= await SeedDepartmentsAsync(dbContext, logger);

        if (seeded)
        {
            await dbContext.SaveChangesAsync();
            logger.LogInformation("Tenant seed data committed successfully");
        }
    }

    private static async Task<bool> SeedTenantsAsync(TenantDbContext dbContext, ILogger logger)
    {
        var seeded = false;

        // Admini — platform admin tenant
        if (!await dbContext.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == AdminTenantId))
        {
            var tenantCode = TenantCode.CreateNew().Value!;
            var slug = SharedKernel.Domain.ValueObjects.TenantSlug.Create("admini").Value!;
            var tenant = CreateTenant(AdminTenantId, tenantCode, "Admini", slug);
            dbContext.Tenants.Add(tenant);
            seeded = true;
            logger.LogInformation("Seeded tenant: Admini (admini)");
        }

        // Acme Corporation — sample enterprise tenant
        if (!await dbContext.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == TenantId))
        {
            var tenantCode = TenantCode.CreateNew().Value!;
            var slug = SharedKernel.Domain.ValueObjects.TenantSlug.Create("acme").Value!;
            var tenant = CreateTenant(TenantId, tenantCode, "Acme Corporation", slug);
            dbContext.Tenants.Add(tenant);
            seeded = true;
            logger.LogInformation("Seeded tenant: Acme Corporation (acme)");
        }

        return seeded;
    }

    private static async Task<bool> SeedDepartmentsAsync(TenantDbContext dbContext, ILogger logger)
    {
        var seeded = false;

        // Admin department (admin tenant)
        if (!await dbContext.Departments.IgnoreQueryFilters().AnyAsync(d => d.Id == AdminDepartmentId))
        {
            var name = DepartmentName.Create("Admin").Value!;
            var dept = CreateDepartment(AdminDepartmentId, AdminTenantId, name, "Platform administrators");
            dbContext.Departments.Add(dept);
            seeded = true;
            logger.LogInformation("Seeded department: Admin (admin tenant)");
        }

        // Development department (acme tenant)
        if (!await dbContext.Departments.IgnoreQueryFilters().AnyAsync(d => d.Id == DevelopmentDepartmentId))
        {
            var name = DepartmentName.Create("Development").Value!;
            var dept = CreateDepartment(DevelopmentDepartmentId, TenantId, name, null);
            dbContext.Departments.Add(dept);
            seeded = true;
            logger.LogInformation("Seeded department: Development (acme)");
        }

        // Support department (acme tenant)
        if (!await dbContext.Departments.IgnoreQueryFilters().AnyAsync(d => d.Id == SupportDepartmentId))
        {
            var name = DepartmentName.Create("Support").Value!;
            var dept = CreateDepartment(SupportDepartmentId, TenantId, name, null);
            dbContext.Departments.Add(dept);
            seeded = true;
            logger.LogInformation("Seeded department: Support (acme)");
        }

        return seeded;
    }

    private static Tenant CreateTenant(Guid id, TenantCode tenantCode, string name,
        SharedKernel.Domain.ValueObjects.TenantSlug slug)
    {
        var constructor = typeof(Tenant).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c =>
            {
                var p = c.GetParameters();
                return p.Length == 7
                    && p[0].ParameterType == typeof(Guid)
                    && p[1].ParameterType == typeof(Guid)
                    && p[2].ParameterType == typeof(TenantCode);
            });

        var tenant = (Tenant)constructor.Invoke(
            [id, id, tenantCode, name, slug, TenantStatus.Active, PlanTier.Enterprise]);
        return tenant;
    }

    private static Department CreateDepartment(Guid id, Guid tenantId, DepartmentName name, string? description)
    {
        var constructor = typeof(Department).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c =>
            {
                var p = c.GetParameters();
                return p.Length == 4
                    && p[0].ParameterType == typeof(Guid)
                    && p[1].ParameterType == typeof(Guid)
                    && p[2].ParameterType == typeof(DepartmentName);
            });

        var dept = (Department)constructor.Invoke([id, tenantId, name, description]);
        return dept;
    }
}
