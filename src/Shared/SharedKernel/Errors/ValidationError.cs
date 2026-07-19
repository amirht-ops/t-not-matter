namespace SharedKernel.Errors;

public sealed record ValidationError(string PropertyName, string Message);
