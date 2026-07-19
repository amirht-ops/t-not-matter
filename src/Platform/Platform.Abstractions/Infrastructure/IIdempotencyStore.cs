namespace Platform.Abstractions.Infrastructure;

public interface IIdempotencyStore
{
    Task<bool> TryRegisterAsync(string key, CancellationToken cancellationToken = default);
    Task<T?> GetResponseAsync<T>(string key, CancellationToken cancellationToken = default);
    Task StoreResponseAsync<T>(string key, T response, CancellationToken cancellationToken = default);
}
