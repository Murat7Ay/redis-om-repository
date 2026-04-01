using CrudApp.ChangeTracking;
using CrudApp.Entity;
using Redis.OM.Modeling;

namespace CrudApp.Tests;

[Document(StorageType = StorageType.Json, Prefixes = new[] { "Test" })]
public class TestEntity : BaseEntity<TestEntity>
{
    [Indexed(CaseSensitive = false)]
    public string Name { get; set; } = string.Empty;

    [Searchable]
    public string Description { get; set; } = string.Empty;

    [SensitiveProperty]
    public string Secret { get; set; } = string.Empty;

    public decimal Price { get; set; }
}
