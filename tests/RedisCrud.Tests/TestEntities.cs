using Redis.OM.Modeling;

namespace RedisCrud.Tests;

public enum WidgetKind { Small, Large }

public class Address
{
    public string City { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
}

[Document(StorageType = StorageType.Json, Prefixes = ["TestWidget"])]
public class Widget : Entity
{
    [Indexed(CaseSensitive = false)]
    public string Name { get; set; } = string.Empty;

    [Searchable]
    public string Description { get; set; } = string.Empty;

    [Searchable]
    public string Notes { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public WidgetKind Kind { get; set; }

    public List<string> Tags { get; set; } = [];

    public Dictionary<string, int> Counters { get; set; } = [];

    public Address? Address { get; set; }

    [SensitiveProperty]
    public string? Secret { get; set; }

    /// <summary>Left at DateTime.MinValue on purpose: Redis OM's default converter throws for it on UTC+ hosts.</summary>
    public DateTime Released { get; set; }
}

/// <summary>Used by tests that need an index nobody else writes to.</summary>
[Document(StorageType = StorageType.Json, Prefixes = ["TestGadget"])]
public class Gadget : Entity
{
    [Indexed]
    public string Name { get; set; } = string.Empty;

    [Searchable]
    public string Description { get; set; } = string.Empty;
}

public class NotADocument : Entity;
