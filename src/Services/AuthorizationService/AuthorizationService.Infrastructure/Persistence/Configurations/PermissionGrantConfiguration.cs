using AuthorizationService.Domain.Aggregates.PermissionGrant;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorizationService.Infrastructure.Persistence.Configurations;

public sealed class PermissionGrantConfiguration : IEntityTypeConfiguration<PermissionGrant>
{
    public void Configure(EntityTypeBuilder<PermissionGrant> builder)
    {
        builder.ToTable("authorization_permission_grants");
        builder.HasKey(grant => new { grant.TenantId, grant.Id });
        builder.HasIndex(grant => new { grant.TenantId, grant.RoleId, grant.PermissionId });
        builder.Property(grant => grant.Id).ValueGeneratedNever();
        builder.Property(grant => grant.TenantId).ValueGeneratedNever();
        builder.Property(grant => grant.RoleId).HasConversion(x => x.Value, v => RoleId.From(v)).IsRequired();
        builder.Property(grant => grant.PermissionId).HasConversion(x => x.Value, v => PermissionId.From(v)).IsRequired();
        builder.Property(grant => grant.GrantedBy).HasConversion(x => x.Value, v => UserId.From(v)).IsRequired();
        builder.Property(grant => grant.Version).IsConcurrencyToken();
        builder.Ignore(grant => grant.IsActive);
        builder.Ignore(grant => grant.DomainEvents);
    }
}
