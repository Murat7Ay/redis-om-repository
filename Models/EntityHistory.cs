using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace CrudApp.Models;

public class History
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("entity_name")]
    public string EntityName { get; set; } = string.Empty;

    [JsonPropertyName("records")]
    public List<HistoryRecord> Records { get; set; } = new();
}

public class HistoryRecord
{
    [JsonPropertyName("stream_id")]
    public string StreamId { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("records")]
    public List<ChangeRecord> Records { get; set; } = new();
}

public class ChangeRecord
{
    [JsonPropertyName("prop_name")]
    public string PropertyName { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    public static implicit operator ChangeRecord(NameValueEntry entry)
    {
        return new ChangeRecord
        {
            PropertyName = entry.Name.ToString(),
            Value = entry.Value.ToString()
        };
    }
}
