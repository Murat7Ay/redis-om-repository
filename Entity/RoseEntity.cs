using Redis.OM.Modeling;

namespace CrudApp.Entity;

[Document(StorageType = StorageType.Json,Prefixes = new []{"Rose"})]
public class RoseEntity : IEntity<RoseEntity>, IVersionAbleEntity
{
    [RedisIdField] 
    [Indexed] 
    public string Id { get; set; } = null!;
    public int Version { get; set; } 
    [Indexed(CaseSensitive = false)]
    public string Name { get; set; } = string.Empty;
    [Searchable]
    public string Description { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new List<string>();
    public IList<KeyValuePair<string, string>> GetChanges(RoseEntity oldOne)
    {
        List<KeyValuePair<string, string>> changes = new List<KeyValuePair<string, string>>();
        if (Name != oldOne.Name)
        {
            changes.Add(new KeyValuePair<string, string>(nameof(Name),oldOne.Name));
        }
        if (Description != oldOne.Description)
        {
            changes.Add(new KeyValuePair<string, string>(nameof(Description),oldOne.Description));
        }

        return changes;
    }
}