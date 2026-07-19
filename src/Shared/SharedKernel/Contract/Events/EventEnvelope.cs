using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Contract.Events;

public sealed record EventEnvelope(
    [property: JsonPropertyName("eventId")]
    Guid EventId,
    [property: JsonPropertyName("tenantId")]
    Guid TenantId,
    [property: JsonPropertyName("correlationId")]
    Guid CorrelationId,
    [property: JsonPropertyName("eventType")]
    string EventType,
    [property: JsonPropertyName("version")]
    int Version,
    [property: JsonPropertyName("occurredAt")]
    DateTimeOffset OccurredAt,
    [property: JsonPropertyName("payload")]
    JsonElement Payload,
    [property: JsonPropertyName("causationId")]
    Guid? CausationId = null);
