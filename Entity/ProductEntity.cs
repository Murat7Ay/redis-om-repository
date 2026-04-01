using Redis.OM.Modeling;

namespace CrudApp.Entity;

[ApiPolicy("moderator")]
[Document(StorageType = StorageType.Json, Prefixes = new[] { "Product" })]
public class ProductEntity : BaseEntity<ProductEntity>
{
    [Indexed(CaseSensitive = false)]
    public string Name { get; set; } = string.Empty;

    [Searchable]
    public string Description { get; set; } = string.Empty;

    [Indexed]
    public decimal Price { get; set; }

    [Indexed]
    public string Category { get; set; } = string.Empty;

    [Indexed]
    public int Stock { get; set; }

    public List<string> Tags { get; set; } = new();

    public List<string> Images { get; set; } = new();
}
