# RedisCrud

Convention-based CRUD endpoints for documents stored in **Redis 8** (JSON + Query Engine + Streams),
for ASP.NET Core 10 minimal APIs.

> **Preview.** The public API may change between preview releases.

- **Atomic writes**: version compare-and-set, `JSON.SET` and the history `XADD` run as one Lua script.
  Concurrent writers with the same version produce exactly one winner.
- **Server-owned system fields**: id, version, audit and soft-delete fields cannot be set by clients.
- **History**: one event per write with operation, version, actor and old/new values per JSON path.
- **Search**: injection-safe full-text search over `[Searchable]` properties, prefix matching, Turkish casing.
- **HTTP done properly**: `ETag`/`If-Match`, 201/204/409/412/428, RFC 9457 ProblemDetails.
- **Authorization**: read / write / admin policies per entity, authenticated by default.

## Install

```bash
dotnet add package RedisCrud --prerelease
```

Requires .NET 10 and Redis 8 (or Redis Stack with JSON and the Query Engine). Single shard only.

## Use

```csharp
[Document(StorageType = StorageType.Json, Prefixes = ["Product"])]
public class ProductEntity : Entity
{
    [Required, StringLength(200)]
    [Indexed(CaseSensitive = false)] public string Name { get; set; } = "";
    [Searchable] public string Description { get; set; } = "";
    [Range(0, 1_000_000)]
    [Indexed(Sortable = true)] public decimal Price { get; set; }
}
```

```csharp
builder.Services.AddRedisCrud(builder.Configuration.GetSection("Crud")); // Crud:ConnectionString, ...
builder.Services.AddAuthentication(/* your scheme */);
builder.Services.AddAuthorization(/* your policies */);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapCrud<ProductEntity>("/products", o =>
{
    o.ReadPolicy = "reader";
    o.WritePolicy = "editor";
    o.AdminPolicy = "admin";   // restore, purge, history
});
```

| Method | Route | Policy |
|---|---|---|
| `GET` | `/products?offset=&limit=` | read |
| `GET` | `/products/search?q=` | read |
| `GET` | `/products/{id}` | read |
| `POST` | `/products` | write |
| `PUT` | `/products/{id}` (requires `If-Match` or `rowVersion`) | write |
| `DELETE` | `/products/{id}` | write |
| `POST` | `/products/{id}/restore` | admin |
| `POST` | `/products/{id}/purge` | admin |
| `GET` | `/products/{id}/history` | admin |

`RedisEntityStore<T>` can also be injected and used directly for hand-written endpoints.
All writes to entity keys must go through it.

Full documentation, design notes and a sample application with JWT authentication:
https://github.com/Murat7Ay/RedisCrud
