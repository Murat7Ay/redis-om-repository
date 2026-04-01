using Redis.OM.Modeling;

namespace CrudApp.Entity;

[ApiPolicy("root")]
[Document(StorageType = StorageType.Json, Prefixes = new[] { "Rose" })]
public class RoseEntity : BaseEntity<RoseEntity>
{
    [Indexed(CaseSensitive = false)]
    public string Name { get; set; } = string.Empty;

    [Searchable]
    public string Description { get; set; } = string.Empty;

    public List<string> Images { get; set; } = new();
}
