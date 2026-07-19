namespace SharedKernel.Domain.Guards;

public static class Guard
{
    public static string NotEmpty(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", parameterName) : value.Trim();

    public static Guid NotEmpty(Guid value, string parameterName) =>
        value == Guid.Empty ? throw new ArgumentException("Value is required.", parameterName) : value;
}