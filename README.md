
# Redis-OM Repository

Simple crud apis with redis-om. 

## Features

- Entity history
- Optimistic lock
- Trace
- Jwt
- Swagger support
- Role based policy




## Installation

Clone repo and run docker for redis-stack

```bash
  docker run -d --name redis-stack -p 6379:6379 -p 8001:8001 redis/redis-stack:latest
```
    

## Usage/Examples

### Entity 
```csharp
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
```

### Minimal apis
```csharp
builder.Services.AddSingleton(typeof(IRepository<>), typeof(Repository<>));

app.MapPut("/rose", (IRepository<RoseEntity> repository, RoseEntity entity) => repository.Save(entity))
    .RequireAuthorization("root");
app.MapPost("/rose", (IRepository<RoseEntity> repository, RoseEntity entity) => repository.Update(entity))
    .RequireAuthorization("root");
app.MapDelete("/rose", (IRepository<RoseEntity> repository, string id) => repository.Delete(id))
    .RequireAuthorization("root");
app.MapGet("/rose", (IRepository<RoseEntity> repository) => repository.Get()).RequireAuthorization("root");
app.MapGet("/rose/{id}/history", (IRepository<RoseEntity> repository, string id) => repository.GetHistory(id))
    .RequireAuthorization("root");
```

