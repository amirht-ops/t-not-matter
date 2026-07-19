namespace SharedKernel.Application;

public interface IRetryableRequest
{
    int MaxRetries => 3;
}
