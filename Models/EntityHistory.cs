using StackExchange.Redis;

namespace CrudApp.Models;

public class History
{
    public string id { get; set; }
    public string entity_name { get; set; }
    public List<HistoryRecord> records { get; set; }
}

public class HistoryRecord
{
    public string stream_id { get; set; }
    public DateTime date { get; set; }
    public List<Record> records { get; set; }
    
    
}

public class Record
{
    public string prop_name { get; set; }
    public string value { get; set; }

    public static implicit operator Record(NameValueEntry entry)
    {
        return new Record
        {
            prop_name = entry.Name,
            value = entry.Value
        };
    }
}