using System.Text.Json.Serialization;

namespace SharedKernel.Caching;

public sealed record SharedTenantCacheDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("slug")]
    public string Slug { get; init; } = string.Empty;

    public SharedTenantCacheDto() { }

    [JsonConstructor]
    public SharedTenantCacheDto(Guid id, string slug)
    {
        Id = id;
        Slug = slug;
    }
}
