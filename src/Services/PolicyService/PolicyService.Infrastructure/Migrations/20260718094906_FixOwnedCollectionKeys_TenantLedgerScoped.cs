using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolicyService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixOwnedCollectionKeys_TenantLedgerScoped : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_usage_counters",
                schema: "policy",
                table: "usage_counters");

            migrationBuilder.DropIndex(
                name: "IX_usage_counters_TenantId_UsageLedgerId",
                schema: "policy",
                table: "usage_counters");

            migrationBuilder.DropPrimaryKey(
                name: "PK_recovery_entries",
                schema: "policy",
                table: "recovery_entries");

            migrationBuilder.DropIndex(
                name: "IX_recovery_entries_TenantId_DebtLedgerId",
                schema: "policy",
                table: "recovery_entries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_debt_entries",
                schema: "policy",
                table: "debt_entries");

            migrationBuilder.DropIndex(
                name: "IX_debt_entries_TenantId_DebtLedgerId",
                schema: "policy",
                table: "debt_entries");

            migrationBuilder.AddPrimaryKey(
                name: "PK_usage_counters",
                schema: "policy",
                table: "usage_counters",
                columns: new[] { "TenantId", "UsageLedgerId", "action", "window" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_recovery_entries",
                schema: "policy",
                table: "recovery_entries",
                columns: new[] { "TenantId", "DebtLedgerId", "action", "window" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_debt_entries",
                schema: "policy",
                table: "debt_entries",
                columns: new[] { "TenantId", "DebtLedgerId", "action" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_usage_counters",
                schema: "policy",
                table: "usage_counters");

            migrationBuilder.DropPrimaryKey(
                name: "PK_recovery_entries",
                schema: "policy",
                table: "recovery_entries");

            migrationBuilder.DropPrimaryKey(
                name: "PK_debt_entries",
                schema: "policy",
                table: "debt_entries");

            migrationBuilder.AddPrimaryKey(
                name: "PK_usage_counters",
                schema: "policy",
                table: "usage_counters",
                columns: new[] { "action", "window" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_recovery_entries",
                schema: "policy",
                table: "recovery_entries",
                columns: new[] { "action", "window" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_debt_entries",
                schema: "policy",
                table: "debt_entries",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "IX_usage_counters_TenantId_UsageLedgerId",
                schema: "policy",
                table: "usage_counters",
                columns: new[] { "TenantId", "UsageLedgerId" });

            migrationBuilder.CreateIndex(
                name: "IX_recovery_entries_TenantId_DebtLedgerId",
                schema: "policy",
                table: "recovery_entries",
                columns: new[] { "TenantId", "DebtLedgerId" });

            migrationBuilder.CreateIndex(
                name: "IX_debt_entries_TenantId_DebtLedgerId",
                schema: "policy",
                table: "debt_entries",
                columns: new[] { "TenantId", "DebtLedgerId" });
        }
    }
}
