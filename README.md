# Redis OM Repository

An EF-inspired generic CRUD API framework built with ASP.NET Core 9 and [Redis OM .NET](https://github.com/redis/redis-om-dotnet). Define an entity class, inherit from `BaseEntity<T>`, and get fully-featured REST endpoints with zero wiring.

## Features

- **Auto-generated CRUD endpoints** -- inherit `BaseEntity<T>`, get PUT/POST/GET/DELETE routes automatically
- **Audit columns** -- `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy` are set automatically
- **Soft-delete by default** -- `DELETE` marks as deleted; `/purge` permanently removes; `/restore` recovers
- **Optimistic concurrency** -- `RowVersion` prevents lost updates (409 Conflict on mismatch)
- **Entity history** -- every update is tracked via Redis Streams with field-level diffs
- **Reflection-based change tracking** -- automatic diff detection; sensitive fields masked with `[SensitiveProperty]`
- **Full-text search** -- generic `GET /{entity}/search?q=` across all `[Searchable]` properties
- **Specification pattern** -- composable query specifications
- **Pagination** -- built-in offset/limit pagination with total count
- **JWT authentication** -- bearer token auth with configurable expiry
- **Role-based authorization** -- `reader`, `moderator`, `root` policies via `[ApiPolicy]` attribute
- **Health check** -- `/health` endpoint verifying Redis connectivity
- **Swagger/OpenAPI** -- interactive API docs at `/swagger`
- **Global exception handling** -- unhandled errors return structured JSON, never raw stack traces
- **Proper HTTP status codes** -- `Result<T>` maps to 200/400/404/409/500 automatically
- **xUnit test suite** -- 29 tests covering repository, change tracking, and result mapping

## Quick Start: Adding a New Entity (5 lines)

```csharp
[ApiPolicy("moderator")]
[Document(StorageType = StorageType.Json, Prefixes = new[] { "Product" })]
public class ProductEntity : BaseEntity<ProductEntity>
{
    [Indexed(CaseSensitive = false)] public string Name { get; set; } = string.Empty;
    [Searchable] public string Description { get; set; } = string.Empty;
    [Indexed] public decimal Price { get; set; }
    [Indexed] public string Category { get; set; } = string.Empty;
    [Indexed] public int Stock { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Images { get; set; } = new();
}
```

That's it. This generates all of these endpoints automatically:

| Method | Route | Description |
|--------|-------|-------------|
| `PUT` | `/product` | Create a new product |
| `POST` | `/product` | Update an existing product |
| `DELETE` | `/product?id={id}` | Soft-delete a product |
| `DELETE` | `/product/purge?id={id}` | Permanently delete a product |
| `POST` | `/product/{id}/restore` | Restore a soft-deleted product |
| `GET` | `/product` | List all active products |
| `GET` | `/product/{id}` | Get a product by ID |
| `GET` | `/product/{offset}/{limit}` | Paginated list |
| `GET` | `/product/{id}/history` | Change history |
| `GET` | `/product/search?q={query}` | Full-text search |

Every entity automatically gets:
- Audit fields: `CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`
- Soft-delete fields: `IsDeleted`, `DeletedAt`, `DeletedBy`
- Optimistic concurrency: `RowVersion`

## What You Get For Free (BaseEntity)

```csharp
public abstract class BaseEntity<T> : IEntity<T>, IAuditableEntity, ISoftDeletable, IVersionable
{
    string Id            // Redis key, auto-generated on insert
    int RowVersion       // Incremented on every update, checked for conflicts
    DateTime CreatedAt   // Set once on insert
    DateTime UpdatedAt   // Updated on every modification
    string CreatedBy     // User from JWT claims (or "system")
    string UpdatedBy     // User from JWT claims (or "system")
    bool IsDeleted       // Soft-delete flag, filtered from queries by default
    DateTime? DeletedAt  // When the entity was soft-deleted
    string? DeletedBy    // Who soft-deleted it
}
```

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker](https://www.docker.com/get-started) (for Redis Stack)

### Option 1: Docker Compose (recommended)

```bash
docker compose up -d
```

Starts both Redis Stack and the API. API at `http://localhost:5259`, Redis Insight at `http://localhost:8001`.

### Option 2: Manual Setup

```bash
docker run -d --name redis-stack -p 6379:6379 -p 8001:8001 redis/redis-stack:latest
dotnet run
```

Open `http://localhost:5259/swagger` for the interactive docs.

## Configuration

```json
{
  "Redis": {
    "ConnectionString": "localhost:6379"
  },
  "ApiSettings": {
    "SecretKey": "your-secret-key-here",
    "PasswordKey": "your-password-key-here",
    "TokenExpiryMinutes": 300,
    "MaxEntityCount": 1000,
    "HistoryMaxLength": 10,
    "SoftDeleteEnabled": true
  }
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `SecretKey` | HMAC key for signing JWT tokens | *(required)* |
| `PasswordKey` | HMAC key for hashing passwords | *(required)* |
| `TokenExpiryMinutes` | JWT token lifetime in minutes | `300` |
| `MaxEntityCount` | Max entities returned before requiring pagination | `1000` |
| `HistoryMaxLength` | Max stream entries per entity history | `10` |
| `SoftDeleteEnabled` | Global soft-delete toggle | `true` |

## Authentication

### Register a User

```http
PUT /user
Content-Type: application/json

{
  "name": "admin",
  "password": "secret",
  "role": "root"
}
```

### Login

```http
POST /login
Content-Type: application/json

{
  "username": "admin",
  "password": "secret"
}
```

Use the returned JWT token in `Authorization: Bearer <token>` header.

## Running Tests

```bash
cd CrudApp.Tests
dotnet test
```

## Architecture

```
Program.cs                          -- Composition root
Extensions/
  ServiceCollectionExtensions.cs    -- Redis, JWT, Swagger DI setup
  ResultExtensions.cs               -- Result<T> -> HTTP status code mapping
Middleware/
  GlobalExceptionMiddleware.cs      -- Catches unhandled exceptions
Entity/
  IEntityId.cs                      -- Base interface with Redis Id
  IEntity<T>.cs                     -- Entity contract with default change tracking
  IAuditableEntity.cs               -- CreatedAt/UpdatedAt/CreatedBy/UpdatedBy
  ISoftDeletable.cs                 -- IsDeleted/DeletedAt/DeletedBy
  IVersionable.cs                   -- RowVersion for optimistic concurrency
  BaseEntity<T>.cs                  -- Abstract base implementing all interfaces
  ApiPolicyAttribute.cs             -- Declarative authorization per entity
ChangeTracking/
  IChangeTracker.cs                 -- Change detection contract
  ReflectionChangeTracker.cs        -- Automatic property-level diff
  SensitivePropertyAttribute.cs     -- Masks sensitive fields in history
Repository/
  IRepository<T>.cs                 -- Generic repository interface
  Repository<T>.cs                  -- Redis OM implementation with audit + soft-delete
Specification/
  ISpecification<T>.cs              -- Query specification contract
  Specification<T>.cs               -- Base class with expression criteria
  SearchSpecification<T>.cs         -- Generic full-text search across [Searchable] props
Service/
  TokenService.cs                   -- JWT generation and password hashing
Settings/
  ApiSettings.cs                    -- Strongly-typed configuration
Models/
  Result.cs                         -- Unified API response envelope
  Pagination.cs                     -- Offset/limit/count metadata
  EntityHistory.cs                  -- Stream-based change history DTOs
  TokenResponse.cs                  -- JWT token wrapper
  UserLoginRequest.cs               -- Login request DTO
CrudApp.Tests/                      -- xUnit test project (29 tests)
```

## License

This project is provided as-is for educational and experimental purposes.
