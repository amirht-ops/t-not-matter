namespace Platform.Abstractions.Tenant;

public interface IRequestContextAccessor
{
    RequestContext Context { get; set; }
}
