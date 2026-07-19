using IdentityService.Domain.Aggregates;
using IdentityService.Domain.Aggregates.Session;
using IdentityService.Domain.Aggregates.User;
using IdentityService.Domain.Services;
using IdentityService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdentityService.Infrastructure.Persistence.Seed;

public static class IdentityDataSeeder
{
    // Admin tenant
    public static readonly Guid AdminTenantId = Guid.Parse("5f9c9b5a-d1b4-4b53-8d60-70f90e5f0ea5");
    public static readonly Guid SuperUserId = Guid.Parse("8f9c9b5a-d1b4-4b53-8d60-70f90e5f0ea6");

    // Acme tenant
    public static readonly Guid TenantId = Guid.Parse("6f9c9b5a-d1b4-4b53-8d60-70f90e5f0ea5");

    // User IDs
    public static readonly Guid SeniorDevUserId = Guid.Parse("a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d");
    public static readonly Guid SupportLeadUserId = Guid.Parse("f5e4d3c2-b1a0-9f8e-7d6c-5b4a3f2e1d0c");

    public static async Task SeedAsync(IdentityDbContext dbContext, IPasswordHasher passwordHasher, ILogger logger)
    {
        var seeded = false;

        seeded |= await SeedUsersAsync(dbContext, passwordHasher, logger);
        seeded |= await SeedAccessRisksAsync(dbContext, logger);
        seeded |= await SeedSessionsAsync(dbContext, logger);

        if (seeded)
        {
            await dbContext.SaveChangesAsync();
            logger.LogInformation("Identity seed data committed successfully");
        }
    }

    private static async Task<bool> SeedUsersAsync(IdentityDbContext dbContext, IPasswordHasher passwordHasher,
        ILogger logger)
    {
        var seeded = false;

        // super — platform administrator
        if (!await dbContext.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == SuperUserId))
        {
            var username = Username.Create("super").Value!;
            var email = Email.Create("admini.super@platform.local").Value!;
            var phone = PhoneNumber.Create("+00000000000").Value!;
            var hash = PasswordHash.FromHash(passwordHasher.Hash("P@ssword123!")).Value!;

            var user = User.Rehydrate(
                SuperUserId, AdminTenantId,
                username, phone, hash, email,
                UserStatus.Active, MfaSettings.Disabled,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0);

            dbContext.Users.Add(user);
            seeded = true;
            logger.LogInformation("Seeded user: super (admini.super@platform.local)");
        }

        // dev — senior developer
        if (!await dbContext.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == SeniorDevUserId))
        {
            var username = Username.Create("dev").Value!;
            var email = Email.Create("senior.dev@acme.com").Value!;
            var phone = PhoneNumber.Create("+12345678901").Value!;
            var hash = PasswordHash.FromHash(passwordHasher.Hash("P@ssword123")).Value!;

            var user = User.Rehydrate(
                SeniorDevUserId, TenantId,
                username, phone, hash, email,
                UserStatus.Active, MfaSettings.Disabled,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0);

            dbContext.Users.Add(user);
            seeded = true;
            logger.LogInformation("Seeded user: dev (senior.dev@acme.com)");
        }

        // lead — support lead / manager
        if (!await dbContext.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == SupportLeadUserId))
        {
            var username = Username.Create("lead").Value!;
            var email = Email.Create("support.lead@acme.com").Value!;
            var phone = PhoneNumber.Create("+12345678902").Value!;
            var hash = PasswordHash.FromHash(passwordHasher.Hash("P@ssword123")).Value!;

            var user = User.Rehydrate(
                SupportLeadUserId, TenantId,
                username, phone, hash, email,
                UserStatus.Active, MfaSettings.Disabled,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0);

            dbContext.Users.Add(user);
            seeded = true;
            logger.LogInformation("Seeded user: lead (support.lead@acme.com)");
        }

        return seeded;
    }

    private static async Task<bool> SeedAccessRisksAsync(IdentityDbContext dbContext, ILogger logger)
    {
        var seeded = false;

        if (!await dbContext.AccessRisks.IgnoreQueryFilters().AnyAsync(a => a.Id == SuperUserId))
        {
            dbContext.AccessRisks.Add(AccessRisk.Create(SuperUserId, AdminTenantId));
            seeded = true;
            logger.LogInformation("Seeded access risk: super (clean slate)");
        }

        if (!await dbContext.AccessRisks.IgnoreQueryFilters().AnyAsync(a => a.Id == SeniorDevUserId))
        {
            dbContext.AccessRisks.Add(AccessRisk.Create(SeniorDevUserId, TenantId));
            seeded = true;
            logger.LogInformation("Seeded access risk: dev (clean slate)");
        }

        if (!await dbContext.AccessRisks.IgnoreQueryFilters().AnyAsync(a => a.Id == SupportLeadUserId))
        {
            dbContext.AccessRisks.Add(AccessRisk.Create(SupportLeadUserId, TenantId));
            seeded = true;
            logger.LogInformation("Seeded access risk: lead (clean slate)");
        }

        return seeded;
    }

    private static async Task<bool> SeedSessionsAsync(IdentityDbContext dbContext, ILogger logger)
    {
        var seeded = false;

        // Active session for super (admin console)
        if (!await dbContext.Sessions.IgnoreQueryFilters()
                .AnyAsync(s => s.UserId == SuperUserId && s.RevokedAt == null && s.ExpiresAt > DateTimeOffset.UtcNow))
        {
            var (session, _) = Session.Create(
                AdminTenantId, SuperUserId,
                TimeSpan.FromDays(30), "127.0.0.1", "SeedData/AdminConsole",
                Guid.NewGuid());
            dbContext.Sessions.Add(session);
            seeded = true;
            logger.LogInformation("Seeded active session: super (30-day admin console)");
        }

        // Active session for dev (dev environment)
        if (!await dbContext.Sessions.IgnoreQueryFilters()
                .AnyAsync(s => s.UserId == SeniorDevUserId && s.RevokedAt == null && s.ExpiresAt > DateTimeOffset.UtcNow))
        {
            var (session, _) = Session.Create(
                TenantId, SeniorDevUserId,
                TimeSpan.FromDays(7), "10.0.0.1", "SeedData/DevEnvironment",
                Guid.NewGuid());
            dbContext.Sessions.Add(session);
            seeded = true;
            logger.LogInformation("Seeded active session: dev (7-day dev environment)");
        }

        // Active session for lead (support portal)
        if (!await dbContext.Sessions.IgnoreQueryFilters()
                .AnyAsync(s => s.UserId == SupportLeadUserId && s.RevokedAt == null && s.ExpiresAt > DateTimeOffset.UtcNow))
        {
            var (session, _) = Session.Create(
                TenantId, SupportLeadUserId,
                TimeSpan.FromDays(7), "10.0.0.2", "SeedData/SupportPortal",
                Guid.NewGuid());
            dbContext.Sessions.Add(session);
            seeded = true;
            logger.LogInformation("Seeded active session: lead (7-day support portal)");
        }

        return seeded;
    }
}
