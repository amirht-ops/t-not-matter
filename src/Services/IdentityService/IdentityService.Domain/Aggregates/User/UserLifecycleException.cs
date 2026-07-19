namespace IdentityService.Domain.Aggregates.User;

[System.Obsolete("Use Result pattern with IdentityErrors instead of throwing exceptions for domain flow control.")]
public sealed class UserLifecycleException(string message) : InvalidOperationException(message);
