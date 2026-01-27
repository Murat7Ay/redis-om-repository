using StackExchange.Redis;

namespace CrudApp.Models;

public class History
{
    public string id { get; set; } = string.Empty;
    public string entity_name { get; set; } = string.Empty;
    public List<HistoryRecord> records { get; set; } = new();
}

public class HistoryRecord
{
    public string stream_id { get; set; } = string.Empty;
    public DateTime date { get; set; }
    public List<Record> records { get; set; } = new();
    
    
}

public class Record
{
    public string prop_name { get; set; } = string.Empty;
    public string value { get; set; } = string.Empty;

    public static implicit operator Record(NameValueEntry entry)
    {
        return new Record
        {
            prop_name = entry.Name.ToString(),
            value = entry.Value.ToString()
        };
    }
}