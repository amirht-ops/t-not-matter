using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PendingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_identity_users_TenantId_DepartmentId",
                schema: "Identity",
                table: "identity_users");

            migrationBuilder.DropIndex(
                name: "IX_identity_sessions_TenantId_DepartmentId",
                schema: "Identity",
                table: "identity_sessions");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                schema: "Identity",
                table: "identity_users");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                schema: "Identity",
                table: "identity_sessions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                schema: "Identity",
                table: "identity_users",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                schema: "Identity",
                table: "identity_sessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_identity_users_TenantId_DepartmentId",
                schema: "Identity",
                table: "identity_users",
                columns: new[] { "TenantId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_identity_sessions_TenantId_DepartmentId",
                schema: "Identity",
                table: "identity_sessions",
                columns: new[] { "TenantId", "DepartmentId" });
        }
    }
}
