namespace SharedKernel.Application;

public interface IIdempotentRequest
{
    string IdempotencyKey { get; }
}
