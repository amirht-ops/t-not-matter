namespace SharedKernel.Responses;

public sealed record HealthResponse(
    string Status = "Healthy",
    object? Components = null,
    object? Startup = null);
