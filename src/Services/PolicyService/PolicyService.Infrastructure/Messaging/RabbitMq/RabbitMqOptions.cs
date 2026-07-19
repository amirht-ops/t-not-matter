using System.ComponentModel.DataAnnotations;

namespace PolicyService.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    [Required] public string Host { get; init; } = string.Empty;
    [Range(1, 65535)] public int Port { get; init; } = 5672;
    [Required] public string VirtualHost { get; init; } = "/";
    [Required] public string Username { get; init; } = string.Empty;
    [Required] public string Password { get; init; } = string.Empty;
    [Required] public string ExchangeName { get; init; } = "policy.events";
}
