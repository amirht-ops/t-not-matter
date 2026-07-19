using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Platform.Abstractions.Tenant;

namespace Platform.Infrastructure.Persistence;

public sealed class TenantRlsInterceptor(IServiceProvider serviceProvider)
    : DbCommandInterceptor
{
    private readonly IRequestContextAccessor _accessor =
        serviceProvider.GetRequiredService<IRequestContextAccessor>();

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        SetTenantContext(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await SetTenantContextAsync(command, cancellationToken);
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        SetTenantContext(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await SetTenantContextAsync(command, cancellationToken);
        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        SetTenantContext(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await SetTenantContextAsync(command, cancellationToken);
        return await base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    // The tenant context MUST be applied as a separate command on the same connection and
    // transaction, NOT prepended to the outgoing command text. Prepending "SET LOCAL ...;"
    // injects an extra statement into the command, which shifts Npgsql's per-statement
    // rows-affected mapping. EF Core's modification command batch maps commands positionally
    // to reader statements, so the leading SET (0 rows affected) is attributed to a real
    // INSERT/UPDATE/DELETE, producing a spurious DbUpdateConcurrencyException on every write.
    private string? BuildSetStatement()
    {
        var ctx = _accessor.Context;

        if (ctx.CanBypassTenantIsolation)
            return null;

        if (ctx.TenantId == Guid.Empty)
            return null;

        var setClauses = $"SET LOCAL app.current_tenant_id = '{ctx.TenantId:N}'";

        if (ctx.DepartmentId.HasValue && ctx.DepartmentId.Value != Guid.Empty)
            setClauses += $", app.current_department_id = '{ctx.DepartmentId.Value:N}'";

        return setClauses;
    }

    private void SetTenantContext(DbCommand command)
    {
        // SET LOCAL is only valid inside a transaction block. EF opens a transaction for
        // modification batches (the path that previously broke); non-transactional single
        // reads are covered by EF global query filters and do not need a LOCAL setting.
        if (command.Transaction is null)
            return;

        var setStatement = BuildSetStatement();
        if (setStatement is null)
            return;

        using var setCommand = command.Connection!.CreateCommand();
        setCommand.CommandText = setStatement;
        setCommand.Transaction = command.Transaction;
        setCommand.ExecuteNonQuery();
    }

    private async ValueTask SetTenantContextAsync(DbCommand command, CancellationToken cancellationToken)
    {
        if (command.Transaction is null)
            return;

        var setStatement = BuildSetStatement();
        if (setStatement is null)
            return;

        await using var setCommand = command.Connection!.CreateCommand();
        setCommand.CommandText = setStatement;
        setCommand.Transaction = command.Transaction;
        await setCommand.ExecuteNonQueryAsync(cancellationToken);
    }
}