using System.Net.Sockets;

namespace Platform.Infrastructure.Startup;

internal static class ExceptionClassifier
{
    private static readonly HashSet<Type> TransientTypes =
    [
        typeof(TimeoutException),
        typeof(OperationCanceledException),
        typeof(SocketException),
        typeof(IOException),
    ];

    public static bool IsTransient(Exception ex)
    {
        if (TransientTypes.Contains(ex.GetType()))
            return true;

        var fullName = ex.GetType().FullName;

        if (fullName is null)
            return false;

        if (fullName.StartsWith("Npgsql.", StringComparison.Ordinal))
            return true;

        if (fullName.StartsWith("StackExchange.Redis.", StringComparison.Ordinal))
            return true;

        if (fullName.StartsWith("RabbitMQ.Client.", StringComparison.Ordinal))
            return true;

        if (fullName.StartsWith("MassTransit.", StringComparison.Ordinal))
            return true;

        return false;
    }
}
