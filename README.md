# RedisCrud

[![CI](https://github.com/Murat7Ay/redis-om-repository/actions/workflows/ci.yml/badge.svg)](https://github.com/Murat7Ay/redis-om-repository/actions/workflows/ci.yml)

Convention-based CRUD endpoints for documents stored in **Redis 8** (JSON + Query Engine + Streams),
for ASP.NET Core 10 (.NET 10 LTS). Entities are declared with Redis OM attributes; the framework adds audit fields,
soft delete, optimistic concurrency, per-entity change history and full-text search, and maps
HTTP endpoints with explicit authorization.

It is a good fit for **reference data, catalogs, admin back-offices and internal tools** where the
resource really is "a document with a lifecycle". It is not a domain layer: entities with business
invariants, cross-entity transactions or fields clients must never set need hand-written endpoints
(the store is usable on its own for that, see `samples/CrudApp/Auth`).

## Quick start

Requires the .NET 10 SDK and Docker.

```bash
docker run -d --name redis -p 6379:6379 redis:8
dotnet run --project samples/CrudApp          # Development: http://localhost:5259/swagger
```

Development configuration ships a dev-only signing key and a bootstrap root user
(`admin` / `dev-admin-password`, see `samples/CrudApp/appsettings.Development.json`).
The app refuses to start outside Development with that key, or with no key.

With Docker Compose (Production mode, secrets from the environment):

```bash
AUTH_SIGNING_KEY="$(openssl rand -base64 48)" BOOTSTRAP_ADMIN_PASSWORD='change-me' docker compose up -d
```

## Defining an entity

```csharp
[Document(StorageType = StorageType.Json, Prefixes = ["Product"], Language = "turkish")]
public class ProductEntity : Entity
{
    [Required, StringLength(200, MinimumLength = 1)]
    [Indexed(CaseSensitive = false)] public string Name { get; set; } = "";
    [StringLength(4000)]
    [Searchable] public string Description { get; set; } = "";
    [Range(0, 1_000_000)]
    [Indexed(Sortable = true)] public decimal Price { get; set; }
    public List<string> Tags { get; set; } = [];
}
```

```csharp
builder.Services.AddRedisCrud(builder.Configuration.GetSection("Crud"));
// ...
app.MapCrud<ProductEntity>("/products", o =>
{
    o.ReadPolicy = "reader";      // GET list / by id / search
    o.WritePolicy = "moderator";  // POST, PUT, DELETE
    o.AdminPolicy = "root";       // restore, purge, history
});
```

Registration is explicit: no assembly scanning, no runtime `MakeGenericMethod`. The index for every
mapped entity (and every `AddEntity<T>()`) is created at startup; if the attributes changed, the stale
index is dropped and recreated (documents are kept and re-indexed in the background), or startup fails
when `Crud:RecreateStaleIndexes` is `false`.

DataAnnotations on the entity are enforced on `POST` and `PUT` (400 `ValidationProblem`). The framework runs this check
itself (an endpoint filter), so it works for any `T` regardless of the host's validation setup; the sample's
auth DTOs use .NET 10's built-in `AddValidation()`.

## Endpoints

| Method | Route | Policy | Success | Notes |
|---|---|---|---|---|
| `GET` | `/products?offset=0&limit=20` | read | 200 `Page<T>` | Active entities ordered by `CreatedAt`. `limit` ≤ `MaxPageSize`, `offset+limit` ≤ 10 000. |
| `GET` | `/products/search?q=...&offset=&limit=` | read | 200 `Page<T>` | Only mapped when the entity has `[Searchable]` properties. Ordered by relevance. |
| `GET` | `/products/{id}` | read | 200 + `ETag` | 404 for missing and soft-deleted entities. |
| `POST` | `/products` | write | 201 + `Location` + `ETag` | Server generates the id (UUIDv7). |
| `PUT` | `/products/{id}` | write | 200 + `ETag` | Full replacement. Requires `If-Match` **or** `rowVersion` in the body (else 428). |
| `DELETE` | `/products/{id}` | write | 204 | Soft delete (or purge when `Crud:SoftDelete=false`). Optional `If-Match`. |
| `POST` | `/products/{id}/restore` | admin | 200 | |
| `POST` | `/products/{id}/purge` | admin | 204 | Removes the document **and its history**. Irreversible. |
| `GET` | `/products/{id}/history?count=50` | admin | 200 | Newest first. Works for soft-deleted entities. |

Errors are RFC 9457 `application/problem+json`.

| Status | When |
|---|---|
| 400 | Validation failure, malformed JSON, invalid paging |
| 401 / 403 | Missing/invalid token / policy not satisfied |
| 404 | Unknown id, soft-deleted (except history/restore/purge) |
| 409 | `rowVersion` in the body is stale (response has `currentVersion`); update/delete of a deleted entity; restore of a live one |
| 412 | `If-Match` does not match the current version |
| 428 | `PUT` without any precondition |

There is no `PATCH` yet; `PUT` replaces every client-owned field, so send the whole document you got from `GET`.

## What the framework owns

`Entity` declares the server-owned fields. Client values for them are **ignored** on create and replace:

| Field | Meaning |
|---|---|
| `Id` | UUIDv7 (32 hex chars), generated on create |
| `RowVersion` | Starts at 1, +1 on every write (update, delete, restore). Exposed as a strong `ETag`. |
| `CreatedAt` / `CreatedBy` | Set once. `CreatedBy` is the token's `sub` claim (stable user id), not the display name. |
| `UpdatedAt` / `UpdatedBy` | Every write |
| `IsDeleted` / `DeletedAt` / `DeletedBy` | Only changed by DELETE / restore |

All other public properties are client-writable. That is the contract of a generic CRUD endpoint;
if an entity has a field clients must not control, do not expose it through `MapCrud`.

`[SensitiveProperty]` makes a property **write-only over HTTP** (never serialized in responses),
**masked** (`***`) in history, and **kept** on `PUT` when the client sends `null`.

## Concurrency

Every write is one Lua script on the server: it compares the stored `RowVersion` with the expected
one, writes the document (`JSON.SET`) and appends the history event (`XADD`) atomically. Concurrent
writers with the same version produce exactly one success; the others get 412/409. A write that does
not change any client-owned field is a no-op (no version bump, no history entry).

This holds as long as **all writes go through `RedisEntityStore<T>`**. `store.Query()` exposes Redis OM
LINQ for application-specific reads; do not use Redis OM's `Insert`/`Update` on these keys.

## History

Key: `history:{Prefix}:{id}` (a Redis Stream, outside the index prefix). One entry per write:

```json
{ "eventId": "1790869343119-0", "at": "...", "operation": "Update", "version": 2, "actor": "<sub>",
  "changes": [ { "path": "Name", "old": "Kalem", "new": "Silgi" },
               { "path": "Address.City", "old": "Ankara", "new": "İzmir" },
               { "path": "Tags", "old": ["a"], "new": ["a","b"] } ] }
```

- Diffs are computed on the stored JSON: nested objects get dotted paths, arrays are recorded as whole
  values, dictionary entries per key, `null` is distinct from `""`, numbers compare by value (`10.5 == 10.50`),
  output is culture-invariant.
- `Create` events contain every client field, so history can be replayed forward **while the create
  event is still retained**. Retention is `Crud:HistoryMaxLength` events per entity (exact trim, default 100;
  0 disables history).
- Purge deletes the history stream (right-to-erasure friendly); the purge itself is logged by the application.
- Streams are per entity: ordering is total within one entity, there is no global ordering across entities.
- This is an **operational change log, not a tamper-evident audit trail**: anyone with Redis write access can
  alter it, retention trims it, and Redis replication is asynchronous. Ship it to an append-only store if you
  need compliance-grade audit.

## Search

`GET /search?q=` splits `q` into letter/digit terms (query syntax cannot be injected; at most 8 terms),
requires **every** term to match at least one `[Searchable]` field, and prefix-matches terms of 2+
characters. Results are ranked by the Query Engine's default scorer.

- Turkish: prefix matching finds suffixed forms (`kalem` → `kalemler`). Each term is also tried in its
  Turkish-cased forms, so `KIRMIZI` finds `kırmızı` and `istanbul`/`ISTANBUL` find `İstanbul`.
  Not supported: ASCII-folded input (`ilik` does not find `ılık`) — that needs a folded copy of the text in the index.
- `[Document(Language = "turkish")]` selects the Turkish stemmer for the index.
- No fuzzy matching. Prefix expansion is bounded by the server's `MAXEXPANSIONS`; very short prefixes on
  large corpora can return incomplete results.
- Paging is `LIMIT offset count`: cost grows with offset and the server caps `offset+limit` at
  `MAXSEARCHRESULTS` (10 000 by default), which the API enforces. Narrow the query instead of paging deeper.

## Redis requirements and operational notes

- Redis 8 (Open Source includes JSON and the Query Engine). Tested on Redis 8.x; Redis Stack 7.2+ ships the
  same modules but is not covered by the test suite.
- **Single shard / no Redis Cluster.** The write script touches the document key and its history key,
  which hash to different slots.
- Enable AOF (`appendonly yes`) if history matters; the compose file does. Replication is asynchronous:
  a failover can lose acknowledged writes, including history.
- The connection is lazy with `AbortOnConnectFail=false`; `/health` reports Redis status.

## Configuration

`Crud` section (framework):

| Key | Default | |
|---|---|---|
| `ConnectionString` | `localhost:6379` | StackExchange.Redis connection string |
| `HistoryMaxLength` | `100` | Events per entity; 0 disables history |
| `SoftDelete` | `true` | `false` makes DELETE purge |
| `DefaultPageSize` / `MaxPageSize` | `20` / `100` | |
| `RecreateStaleIndexes` | `true` | `false`: fail startup on index drift |

`Auth` section (sample application):

| Key | Default | |
|---|---|---|
| `SigningKey` | — | Required, ≥ 32 bytes. Use user-secrets or `Auth__SigningKey`. |
| `Issuer` / `Audience` | `crudapp` | Validated |
| `TokenLifetimeMinutes` | `60` | 1–1440 |
| `AuthRequestsPerMinute` | `10` | Per client IP, `/auth/*` |
| `BootstrapAdmin:Name` / `:Password` | — | Creates a root user at startup if missing |

## Authentication (sample application)

Authentication is an application concern; the sample shows one way to do it:

- `POST /auth/register` `{name, password}` → always role `reader`. Names are unique case-insensitively
  (reserved atomically with `SET NX`).
- `POST /auth/login` `{name, password}` → `{accessToken, tokenType, expiresAt}`. Passwords are hashed with
  ASP.NET Core Identity's PBKDF2 hasher; unknown users and wrong passwords both run one hash verification
  and return the same 401.
- `PUT /users/{id}/role` `{role}` and `GET /users` → root only.
- Roles are hierarchical: `root` ⊇ `moderator` ⊇ `reader`.
- Tokens are not revocable before expiry; a role change applies at the next login.

## Project layout

```
src/RedisCrud/                 framework
  Entity.cs                    base type + [SensitiveProperty]
  EntityMetadata.cs            per-type facts (keys, index name, searchable/sensitive fields), reflected once
  Persistence/                 RedisEntityStore<T> (Lua writes, FT.SEARCH reads), SearchQuery, StorageJson
  History/                     JsonDiff, HistoryEvent
  Endpoints/                   MapCrud<T>, sensitive-property response filter
  Hosting/                     AddRedisCrud, options, actor, index provisioning
samples/CrudApp/               host: entities, JWT auth, users, OpenAPI (Development only)
tests/RedisCrud.Tests/         unit + integration (real Redis via Testcontainers) + API tests
```

## Tests

```bash
dotnet test
```

Requires Docker: the fixture starts `redis:8` with Testcontainers. To use an existing, disposable server
instead, set `REDIS_TEST_CONNECTION=host:port` (tests drop and recreate their indexes).

103 tests: JSON diff semantics, search query construction, metadata; store behaviour against real Redis
(compare-and-set under 25 concurrent writers, lifecycle, history content and trimming, paging stability,
search ranking/injection/Turkish casing, index drift); and HTTP behaviour (authorization matrix, role
hierarchy, forged tokens, over-posting, ETag/If-Match, validation, rate limiting, startup key checks,
OpenAPI exposure).

## Breaking changes from the previous version

| Before | Now |
|---|---|
| `PUT /product` create, `POST /product` update | `POST /products` create, `PUT /products/{id}` replace |
| `DELETE /product?id=`, `DELETE /product/purge?id=` | `DELETE /products/{id}`, `POST /products/{id}/purge` |
| `GET /product/{offset}/{limit}`, unpaged `GET /product` | `GET /products?offset=&limit=` (always paged) |
| `Result<T>` envelope with `ReturnType` | Resource / `Page<T>` bodies, ProblemDetails errors |
| `BaseEntity<T>` + 4 interfaces | `Entity` |
| `[ApiPolicy]` on the entity, one policy for everything, anonymous if absent | `MapCrud` options per operation class, authenticated by default |
| Assembly scanning | Explicit `MapCrud<T>` / `AddEntity<T>` |
| `Specification<T>`, `IRepository<T>` | `RedisEntityStore<T>` + `Query()` for LINQ reads |
| History: old values only, unreadable through the API | Old and new values, operation, version, actor |
| `PUT /user` (anonymous, any role), `POST /login` | `/auth/register` (reader only), `/auth/login`, root-only role management |
| HMAC-SHA256 password "hash" | PBKDF2 (ASP.NET Core Identity) — existing users must reset passwords |
| `ApiSettings` section | `Crud` and `Auth` sections |
| Redis Stack | Redis 8 |

## License

Provided as-is for educational and experimental purposes.
