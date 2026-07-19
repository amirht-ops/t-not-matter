namespace SharedKernel.Application;

public interface ICachedQuery
{
    string CacheKey { get; }
    TimeSpan? AbsoluteExpirationRelativeToNow { get; }
}
