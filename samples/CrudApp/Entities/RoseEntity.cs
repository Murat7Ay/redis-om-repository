using System.ComponentModel.DataAnnotations;
using Redis.OM.Modeling;
using RedisCrud;

namespace CrudApp.Entities;

[Document(StorageType = StorageType.Json, Prefixes = ["Rose"])]
public class RoseEntity : Entity
{
    [Required, StringLength(200, MinimumLength = 1)]
    [Indexed(CaseSensitive = false)]
    public string Name { get; set; } = string.Empty;

    [StringLength(4000)]
    [Searchable]
    public string Description { get; set; } = string.Empty;

    public List<string> Images { get; set; } = [];
}
