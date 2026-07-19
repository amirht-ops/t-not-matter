namespace Platform.Abstractions.Authorization;

public interface IServiceTokenGenerator
{
    string GenerateServiceToken();
}
