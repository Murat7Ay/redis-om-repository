using System.ComponentModel.DataAnnotations;
using Redis.OM.Modeling;
using RedisCrud;

namespace CrudApp.Entities;

[Document(StorageType = StorageType.Json, Prefixes = ["Product"], Language = "turkish")]
public class ProductEntity : Entity
{
    [Required, StringLength(200, MinimumLength = 1)]
    [Indexed(CaseSensitive = false)]
    public string Name { get; set; } = string.Empty;

    [StringLength(4000)]
    [Searchable]
    public string Description { get; set; } = string.Empty;

    [Range(0, 1_000_000)]
    [Indexed(Sortable = true)]
    public decimal Price { get; set; }

    [StringLength(100)]
    [Indexed]
    public string Category { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    [Indexed]
    public int Stock { get; set; }

    public List<string> Tags { get; set; } = [];

    public List<string> Images { get; set; } = [];
}
