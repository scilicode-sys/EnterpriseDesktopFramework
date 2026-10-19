using System.Text.Json.Serialization;

namespace Framework.Core.Sync;

/// <summary>Charge utile d'une notification LISTEN/NOTIFY entre postes.</summary>
public sealed class SyncNotificationDto
{
    [JsonPropertyName("table")]
    public string Table { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("op")]
    public string Operation { get; set; } = string.Empty;

    [JsonPropertyName("sender")]
    public string Sender { get; set; } = string.Empty;

    [JsonIgnore]
    public SyncOperation? ParsedOperation => Operation switch
    {
        "I" => SyncOperation.Insert,
        "U" => SyncOperation.Update,
        "D" => SyncOperation.Delete,
        _ => null,
    };
}
