using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace Platform.Infrastructure.Persistence.Migrations;

public sealed class RlsMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    INpgsqlSingletonOptions npgsqlSingletonOptions)
    : NpgsqlMigrationsSqlGenerator(dependencies, npgsqlSingletonOptions)
{
    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        base.Generate(operation, model, builder, terminate);

        var isTenantBound = operation.Columns.Any(c => c.Name == "TenantId");

        if (isTenantBound && !IsSystemOutboxTable(operation.Name))
        {
            var schema = string.IsNullOrEmpty(operation.Schema) ? "" : $"\"{operation.Schema}\".";
            var tableName = $"\"{operation.Name}\"";
            var fullTableName = $"{schema}{tableName}";

            builder.AppendLine($"ALTER TABLE {fullTableName} ENABLE ROW LEVEL SECURITY;");

            builder.AppendLine($@"
                CREATE POLICY tenant_isolation_policy ON {fullTableName}
                FOR ALL
                USING (""TenantId"" = (current_setting('app.current_tenant_id', true))::uuid);
            ");
        }
    }

    private static bool IsSystemOutboxTable(string tableName)
    {
        return tableName.Equals("tenant_outbox", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("tenant_dead_letter", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("identity_outbox", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("identity_dead_letter", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("authorization_outbox", StringComparison.OrdinalIgnoreCase)
            || tableName.Equals("authorization_dead_letter", StringComparison.OrdinalIgnoreCase);
    }
}
