using AuthorizationService.Domain.Aggregates.Permission;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorizationService.Infrastructure.Persistence.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("authorization_permissions");
        builder.HasKey(permission => new { permission.TenantId, permission.Id });
        builder.HasIndex(permission => new { permission.TenantId, permission.Key }).IsUnique();
        builder.Property(permission => permission.Id).ValueGeneratedNever();
        builder.Property(permission => permission.TenantId).ValueGeneratedNever();
        builder.Property(permission => permission.Key).HasConversion(key => key.Value, value => PermissionKey.FromDb(value)).HasMaxLength(256);
        builder.Property(permission => permission.Action).HasConversion(action => action.Value, value => AuthorizationAction.Create(value)).HasMaxLength(128);
        builder.Property(permission => permission.ResourceType).HasMaxLength(128);
        builder.Property(permission => permission.Description).HasMaxLength(512);
        builder.Property(permission => permission.PermissionVersion).HasColumnName("Version").IsRequired();
        builder.Property(permission => permission.Version).HasColumnName("ConcurrencyVersion").IsConcurrencyToken();
        builder.Ignore(permission => permission.DomainEvents);
    }
}
