using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PolicyService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "policy");

            migrationBuilder.CreateTable(
                name: "debt_ledgers",
                schema: "policy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_debt_ledgers", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "policies",
                schema: "policy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    condition = table.Column<string>(type: "text", nullable: false),
                    expression = table.Column<string>(type: "text", nullable: false),
                    priority_rank = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    compiled_rego = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policies", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "policy_dead_letter",
                schema: "policy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OutboxMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: true),
                    MovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_dead_letter", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "policy_outbox",
                schema: "policy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CausationId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextRetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_policy_outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "principal_edges",
                schema: "policy",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_principal_edges", x => new { x.TenantId, x.UserId, x.RoleId });
                });

            migrationBuilder.CreateTable(
                name: "principal_nodes",
                schema: "policy",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    node_type = table.Column<int>(type: "integer", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_principal_nodes", x => new { x.TenantId, x.NodeId });
                });

            migrationBuilder.CreateTable(
                name: "quota_policies",
                schema: "policy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    quota_daily = table.Column<long>(type: "bigint", nullable: false),
                    quota_weekly = table.Column<long>(type: "bigint", nullable: false),
                    quota_monthly = table.Column<long>(type: "bigint", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quota_policies", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                schema: "policy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "usage_ledgers",
                schema: "policy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_ledgers", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "debt_entries",
                schema: "policy",
                columns: table => new
                {
                    action = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    DebtLedgerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_debt_entries", x => x.action);
                    table.ForeignKey(
                        name: "FK_debt_entries_debt_ledgers_TenantId_DebtLedgerId",
                        columns: x => new { x.TenantId, x.DebtLedgerId },
                        principalSchema: "policy",
                        principalTable: "debt_ledgers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recovery_entries",
                schema: "policy",
                columns: table => new
                {
                    action = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    window = table.Column<string>(type: "text", nullable: false),
                    remaining = table.Column<long>(type: "bigint", nullable: false),
                    window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DebtLedgerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recovery_entries", x => new { x.action, x.window });
                    table.ForeignKey(
                        name: "FK_recovery_entries_debt_ledgers_TenantId_DebtLedgerId",
                        columns: x => new { x.TenantId, x.DebtLedgerId },
                        principalSchema: "policy",
                        principalTable: "debt_ledgers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usage_counters",
                schema: "policy",
                columns: table => new
                {
                    action = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    window = table.Column<string>(type: "text", nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    window_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsageLedgerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_counters", x => new { x.action, x.window });
                    table.ForeignKey(
                        name: "FK_usage_counters_usage_ledgers_TenantId_UsageLedgerId",
                        columns: x => new { x.TenantId, x.UsageLedgerId },
                        principalSchema: "policy",
                        principalTable: "usage_ledgers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_debt_entries_TenantId_DebtLedgerId",
                schema: "policy",
                table: "debt_entries",
                columns: new[] { "TenantId", "DebtLedgerId" });

            migrationBuilder.CreateIndex(
                name: "IX_debt_ledgers_TenantId_consumer_id",
                schema: "policy",
                table: "debt_ledgers",
                columns: new[] { "TenantId", "consumer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_policy_dead_letter_TenantId_MovedAt",
                schema: "policy",
                table: "policy_dead_letter",
                columns: new[] { "TenantId", "MovedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_policy_outbox_TenantId_ProcessedAt_NextRetryAt",
                schema: "policy",
                table: "policy_outbox",
                columns: new[] { "TenantId", "ProcessedAt", "NextRetryAt" });

            migrationBuilder.CreateIndex(
                name: "IX_principal_edges_TenantId_UserId_RoleId",
                schema: "policy",
                table: "principal_edges",
                columns: new[] { "TenantId", "UserId", "RoleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_principal_nodes_TenantId_NodeId",
                schema: "policy",
                table: "principal_nodes",
                columns: new[] { "TenantId", "NodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recovery_entries_TenantId_DebtLedgerId",
                schema: "policy",
                table: "recovery_entries",
                columns: new[] { "TenantId", "DebtLedgerId" });

            migrationBuilder.CreateIndex(
                name: "IX_usage_counters_TenantId_UsageLedgerId",
                schema: "policy",
                table: "usage_counters",
                columns: new[] { "TenantId", "UsageLedgerId" });

            migrationBuilder.CreateIndex(
                name: "IX_usage_ledgers_TenantId_consumer_id",
                schema: "policy",
                table: "usage_ledgers",
                columns: new[] { "TenantId", "consumer_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "debt_entries",
                schema: "policy");

            migrationBuilder.DropTable(
                name: "policies",
                schema: "policy");

            migrationBuilder.DropTable(
                name: "policy_dead_letter",
                schema: "policy");

            migrationBuilder.DropTable(
                name: "policy_outbox",
                schema: "policy");

            migrationBuilder.DropTable(
                name: "principal_edges",
                schema: "policy");

            migrationBuilder.DropTable(
                name: "principal_nodes",
                schema: "policy");

            migrationBuilder.DropTable(
                name: "quota_policies",
                schema: "policy");

            migrationBuilder.DropTable(
                name: "recovery_entries",
                schema: "policy");

            migrationBuilder.DropTable(
                name: "subscriptions",
                schema: "policy");

            migrationBuilder.DropTable(
                name: "usage_counters",
                schema: "policy");

            migrationBuilder.DropTable(
                name: "debt_ledgers",
                schema: "policy");

            migrationBuilder.DropTable(
                name: "usage_ledgers",
                schema: "policy");
        }
    }
}
