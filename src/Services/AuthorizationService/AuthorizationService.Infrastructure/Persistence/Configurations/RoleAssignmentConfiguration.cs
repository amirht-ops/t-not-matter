using AuthorizationService.Domain.Aggregates.RoleAssignment;
using AuthorizationService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthorizationService.Infrastructure.Persistence.Configurations;

public sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.ToTable("authorization_role_assignments");
        builder.HasKey(assignment => new { assignment.TenantId, assignment.Id });
        builder.HasIndex(assignment => new { assignment.TenantId, assignment.SubjectId, assignment.RoleId });
        builder.HasIndex(assignment => new { assignment.TenantId, assignment.SubjectId })
            .IsUnique()
            .HasFilter("\"RevokedAtUtc\" IS NULL");
        builder.Property(assignment => assignment.Id).ValueGeneratedNever();
        builder.Property(assignment => assignment.TenantId).ValueGeneratedNever();
        builder.Property(assignment => assignment.SubjectId).HasConversion(t => t.Value, v => SubjectId.From(v)).IsRequired();
        builder.Property(assignment => assignment.RoleId).HasConversion(x => x.Value, v => RoleId.From(v)).IsRequired();
        builder.Ignore(assignment => assignment.DepartmentId);
        builder.Property(assignment => assignment.AssignedBy).HasConversion(x => x.Value, v => UserId.From(v)).IsRequired();
        builder.Property(assignment => assignment.Version).IsConcurrencyToken();
        builder.Ignore(assignment => assignment.IsActive);
        builder.Ignore(assignment => assignment.DomainEvents);
    }
}
