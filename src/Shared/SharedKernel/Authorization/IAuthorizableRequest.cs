namespace SharedKernel.Authorization;

public interface IAuthorizableRequest
{
    string Action { get; }
    string Resource { get; }
}
