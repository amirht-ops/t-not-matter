using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropOutboxRls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON \"Identity\".\"identity_outbox\";");
            migrationBuilder.Sql("ALTER TABLE \"Identity\".\"identity_outbox\" DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON \"Identity\".\"identity_dead_letter\";");
            migrationBuilder.Sql("ALTER TABLE \"Identity\".\"identity_dead_letter\" DISABLE ROW LEVEL SECURITY;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Identity\".\"identity_outbox\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("CREATE POLICY tenant_isolation_policy ON \"Identity\".\"identity_outbox\" FOR ALL USING (\"TenantId\" = (current_setting('app.current_tenant_id', true))::uuid);");

            migrationBuilder.Sql("ALTER TABLE \"Identity\".\"identity_dead_letter\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("CREATE POLICY tenant_isolation_policy ON \"Identity\".\"identity_dead_letter\" FOR ALL USING (\"TenantId\" = (current_setting('app.current_tenant_id', true))::uuid);");
        }
    }
}
