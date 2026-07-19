using AuthorizationService.Domain.Aggregates.Role;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorizationService.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("authorization_roles");
        builder.HasKey(role => new { role.TenantId, role.Id });
        builder.HasIndex(role => new { role.TenantId, role.DepartmentId, role.Name }).IsUnique();
        builder.Property(role => role.Id).ValueGeneratedNever();
        builder.Property(role => role.TenantId).ValueGeneratedNever();
        builder.Property(role => role.DepartmentId).IsRequired();
        builder.Property(role => role.Name).HasConversion(name => name.Value, value => RoleName.Create(value)).HasMaxLength(128);
        builder.Property(role => role.Description).HasMaxLength(512);
        builder.Property(role => role.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(role => role.ParentRoleId).HasConversion(x => x!.Value, v => RoleId.From(v)).IsRequired(false);
        builder.Property(role => role.Version).IsConcurrencyToken();
        builder.Ignore(role => role.IsActive);
        builder.Ignore(role => role.DomainEvents);
    }
}
