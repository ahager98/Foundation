# Foundation

Reusable .NET 10 building blocks for multi-tenant microservices. Each project ships as an
independent `Foundation.*` NuGet package, so a service depends on exactly the pieces it needs —
tenancy, EF Core data access, write-through caching, idempotent event handling, and a HIPAA-style
compliance guardrail — without duplicating them.

Nullable reference types and implicit usings are enabled solution-wide. There is no application host
here: every project is a library, and the consumer is always a separate service.

**What to look at first**

- **Tenancy as configuration.** One registration call, `AddFoundationPersistence<TContext>`, selects a
  tenancy strategy (`DatabasePerTenant` or `SharedDatabase`) and a database provider (`SqlServer` or
  `Postgres`) from config, with no code change. See [Tenancy strategy](#tenancy-strategy).
- **Safe updates.** `UpdateTrackedAsync` loads the tracked row, rejects stale row versions, and refuses
  changes to ids, tenant and audit fields.
- **Idempotent event handling.** `IdempotentEventHandler<TItem>` claims each item before any side
  effect and isolates failures per item.
- **Write-through caching.** `WriteThroughCache<T>` evicts on every write, even a failed one, and caps
  the lifetime of entries that hold classified data.
- **Compliance as a test.** `HipaaCheck` fails the build on unclassified personal data, unauthenticated
  endpoints, classified data in log statements, and unlisted egress hosts.

## Packages

| Package | Description |
| --- | --- |
| **Foundation.Exceptions** | Shared domain exception types for all microservices: `ResourceNotFoundException`, `ResourceExistsException`, `ConcurrencyConflictException`, `ResourceInactiveException`, all deriving from `DomainException` (structured, non-PII client fields; the base handler never echoes a free-text message). Namespace is `Foundation.Exceptions` for drop-in compatibility. |
| **Foundation.Tenancy** | Per-tenant connection-string resolution: `ITenantService`, `TenantService` (HTTP route + fallback), the configurable `TenancyStrategy`/`TenancyOptions`, the provider-agnostic `ITenantConnectionStringBuilder` seam (SQL Server default), and the `IServiceCollection` registration extension. |
| **Foundation.DataAccess** | EF Core base types shared across DAL projects: `BaseEntity` (Id/IsActive/audit stamps/rowversion), the `ITenantScoped` marker, `IGenericCrud`/`GenericCrud`, and `ParentReference` for navigation-property wiring. |
| **Foundation.Persistence** | EF Core persistence wiring across two orthogonal config axes — tenancy strategy (`TenantDbContext` with a named tenant query filter + `TenantId` stamping) and database provider (`SqlServer`/`Postgres` with the matching `ITenantConnectionStringBuilder`) — plus the `AddFoundationPersistence<TContext>` registration extension, the provider-aware concurrency-token helper, the safe `UpdateTrackedAsync`/`TrackedChildren` update primitive, and the durable `EfIdempotencyClaimStore`. |
| **Foundation.Dto** | Shared DTO primitives: `LookupModel`, `PagedResultModel`, and query-parameter helpers. |
| **Foundation.Caching** | `HybridCache` wiring: the `AddFoundationCaching` extension (optional Redis L2 + HybridCache registration with a configurable default TTL). Tag-based eviction is available directly on the injected `HybridCache` via `RemoveByTagAsync`. `WriteThroughCache<T>` adds a tenant/sub-scope-scoped write-through cache whose writes always evict. |
| **Foundation.Compliance** | HIPAA-style technical-safeguard guardrail: `[Phi]`/`[Pii]`/`[NotClassified]`/`[Retention]`, a Business Associate inventory, and `HipaaCheck` (unclassified fields, endpoint authorization, classified data in logs, unlisted egress, insecure config, expired waivers) with a PHI-free evidence report. A technical guardrail, not a HIPAA certification. |
| **Foundation.Idempotency** | `IdempotentEventHandler<TItem>` (claim per event item before any side effect, per-item failure isolation) and the atomic `IIdempotencyClaimStore` seam; the durable EF store is in `Foundation.Persistence`. |

## Repository layout

```
src/      one project per package
tests/    matching MSTest test project per package
```

## Tenancy strategy

A service's tenant-isolation topology is chosen by the host via the **`Tenancy:Strategy`** config
value, so the same binaries can be deployed either way. Register persistence with
`builder.Services.AddFoundationPersistence<MyServiceDbContext>(builder.Configuration)` and derive the
context from `TenantDbContext`.

| `Tenancy:Strategy` | Isolation | Connection string (`ConnectionStrings:Tenant`) |
| --- | --- | --- |
| `DatabasePerTenant` *(default)* | One physical database per tenant; enforced at the connection level. | Template with a `{tenantId}` placeholder substituted into the database name, e.g. `Server=sql;Database=App{tenantId}Data;…`. |
| `SharedDatabase` | One database for all tenants; rows isolated by a `TenantId` column via an EF Core global query filter. | A single fixed connection string with **no** placeholder, e.g. `Server=sql;Database=Shared;…`. |

```jsonc
{
  "Tenancy": { "Strategy": "SharedDatabase" },
  "ConnectionStrings": { "Tenant": "Server=sql;Database=Shared;User Id=sa;Password=***;" }
}
```

**Opting entities into shared-database isolation.** Mark tenant-owned entities with
`ITenantScoped` (`class Foo : BaseEntity, ITenantScoped`). In `SharedDatabase` mode
`TenantDbContext` applies a tenant query filter (`TenantId == current tenant`) to every
`ITenantScoped` type and stamps `TenantId` on insert; in `DatabasePerTenant` mode the interface
has no effect.

**Coexists with your soft-delete filter — mind the ordering.** The base uses **EF Core 10 named query
filters**: it adds a named tenant filter (`TenantDbContext.TenantQueryFilterName`) rather than a
second anonymous one. Because EF Core allows only one *anonymous* filter per entity and forbids mixing
anonymous + named filters, the base also **lifts** the filter the derived context already declared (e.g.
a soft-delete `e => e.IsActive`) from anonymous to the named `TenantDbContext.SoftDeleteQueryFilterName`.
The two filters then apply together (`e.IsActive` AND `e.TenantId == currentTenant`) but can be disabled
**independently**. For this to work a derived context that overrides `OnModelCreating` **must apply its own
query filters first and call `base.OnModelCreating(modelBuilder)` last**:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // 1) your own model configuration, including soft-delete filters and TenantId indexes
    foreach (var et in modelBuilder.Model.GetEntityTypes().Where(t => typeof(BaseEntity).IsAssignableFrom(t.ClrType)))
        modelBuilder.Entity(et.ClrType).HasQueryFilter(/* e => e.IsActive */);
    // 2) base last: lifts your filter to a named one and adds the named tenant filter per ITenantScoped entity
    base.OnModelCreating(modelBuilder);
}
```

**Notes & caveats.**
- Reads — including the read-before-update inside `GenericCrud` — are scoped automatically, so
  get/update/soft-delete by id are tenant-safe in shared mode.
- An unresolved tenant id resolves to `0`, which matches no rows (fail-closed); cross-tenant
  writes are rejected at `SaveChanges`.
- `IgnoreQueryFilters()` (no arguments) bypasses **both** the tenant and soft-delete filters — treat it
  as a deliberate, review-gated escape hatch (e.g. a cross-tenant admin read). To drop **only** soft-delete
  while keeping tenant isolation, pass the name:
  `IgnoreQueryFilters([TenantDbContext.SoftDeleteQueryFilterName])`.
- The base adds a supporting `TenantId` index only when the entity doesn't already have one that
  leads with `TenantId`, so a service that pre-declared its index gets no duplicate/rename migration.
- **Migrations are per-service and not in this repo.** Shared-database deployments need the
  `TenantId` column + index on each `ITenantScoped` table; generate the migration under
  `SharedDatabase` so a single migration set also serves `DatabasePerTenant` deployments (which
  carry the column unused).

### Tenant-holder services

The service that owns the tenant registry (e.g. a `Company`/`Tenant` table whose id *is* the tenant
id) adopts shared tenancy the same way, with two rules:

- The **registry** entity itself does **not** implement `ITenantScoped` — it must stay unfiltered so
  it is enumerable, and so creating a tenant works while no tenant is yet resolved
  (`CurrentTenantId == 0`).
- **Cross-tenant / global** entities (users that span tenants, global lookups, user↔tenant membership
  joins) also stay unmarked — they are queried outside a tenant route (e.g. login), where a tenant
  filter would match nothing.
- When seeding `ITenantScoped` children in the same request that creates the tenant, either leave
  `TenantId == 0` (the context stamps it) or call `ITenantService.SetTenantId(newId)` before saving —
  setting a non-zero `TenantId` while the current tenant is still `0` trips the cross-tenant-write guard.

## Building & testing

```bash
dotnet restore Foundation.slnx
dotnet build   Foundation.slnx -c Release
dotnet test    Foundation.slnx -c Release
```

## Consuming the packages

Install a package from the NuGet feed you publish to. The committed [`nuget.config`](nuget.config)
uses `packageSourceMapping` so only `Foundation.*` packages resolve from the Foundation feed —
everything else comes from nuget.org. If your feed needs credentials, supply them from the environment
(`GITHUB_ACTOR` / `GITHUB_TOKEN` for GitHub Packages); never hard-code them in the config file.

```bash
dotnet add package Foundation.Exceptions
```

## Selecting a database provider

`Foundation.Persistence` lets a service choose its EF Core provider (Azure SQL or PostgreSQL) from
configuration — no code change to switch. A single registration call replaces ad-hoc
`AddDbContext` / `UseSqlServer` wiring:

```csharp
// Program.cs
builder.Services.AddFoundationPersistence<MyServiceDbContext>(builder.Configuration);
```

```jsonc
// appsettings.json
"Database": { "Provider": "Postgres" },   // or "SqlServer" (the default when unset)
"ConnectionStrings": {
  // {tenantId} is substituted into the database name per request; the rest is provider-native
  "Tenant": "Host=pg;Database=App{tenantId}Data;Username=app;Password=***"
}
```

`AddFoundationPersistence<TContext>` is the single entry point for **both** orthogonal axes:

1. calls `AddFoundationTenancy(configuration)` — binds `TenancyOptions` from `Tenancy:Strategy`;
2. reads `Database:Provider` (case-insensitive; defaults to `SqlServer`) and registers the matching
   `ITenantConnectionStringBuilder` (`SqlServerTenantConnectionStringBuilder` /
   `PostgresTenantConnectionStringBuilder`);
3. registers `TContext` with the matching EF provider (`UseSqlServer` / `UseNpgsql`, with
   `EnableRetryOnFailure`), resolving the per-tenant connection string from the scoped
   `ITenantService` at request time.

`TenantService` keeps the provider-agnostic concerns — reading the `ConnectionStrings:{key}`
template (`Tenant:ConnectionStringKey`, default `Tenant`) and applying
`Tenant:MaxPoolSize` / `Tenant:MinPoolSize`. The two axes are independent:

- `Tenancy:Strategy = DatabasePerTenant` → it validates the tenant id is a positive integer
  (injection guard) and substitutes `{tenantId}` into the database name.
- `Tenancy:Strategy = SharedDatabase` → one fixed database serves every tenant, so there is no
  substitution and no tenant-id guard; the template is used verbatim (pool sizing only).

Only the driver-specific assembly is delegated to the builder. An unsupported `Database:Provider`
throws at startup.

### Provider portability: concurrency tokens

`BaseEntity.RowVersion` (`byte[]`) is SQL-Server-shaped — it maps to a `rowversion` column.
PostgreSQL has no such type. So that a service can flip `Database:Provider` without changing model
code, call the provider-aware helper from your `OnModelCreating`:

```csharp
modelBuilder.ApplyFoundationConcurrencyTokens(Database);
```

- **SQL Server** — maps `RowVersion` as a `rowversion` concurrency token (`IsRowVersion()`).
- **PostgreSQL** — drops the `byte[] RowVersion` column and uses the always-present system `xmin`
  column as the concurrency token instead.
- **Non-relational** (the in-memory test provider) — no-op.

Detection is by the `RowVersion` property (name + `byte[]` type), so it works regardless of which
`BaseEntity` a service uses.

### Adding another provider

Add a `case` in `PersistenceServiceExtensions.AddFoundationPersistence` and a matching
`ITenantConnectionStringBuilder` implementation (substitute `{tenantId}` into that driver's
database-name keyword, apply pool sizes). No change to `TenantService` is required.

### What each service still owns

Provider selection is wired centrally, but these remain the consuming service's responsibility
(one provider per deployment):

- **Migrations** are provider-specific — generate them against the provider the service deploys
  with; supporting both simultaneously needs separate migration sets (e.g. `Migrations/SqlServer`,
  `Migrations/Postgres`) selected by `Database:Provider` at design time and runtime.
- **Concurrency tokens** — call `modelBuilder.ApplyFoundationConcurrencyTokens(Database)` (above) instead
  of hardcoding `IsRowVersion()`, so the `RowVersion`→`xmin` switch is handled for you.
- **Provider-specific column types** (`datetime2`, `nvarchar`, computed/JSON columns) must be
  conditioned on the active provider in `OnModelCreating`.

## Versioning & publishing

Versioning is tag-driven via [MinVer](https://github.com/adamralph/minver): a `vX.Y.Z` git tag
sets the version for every package; untagged builds get a height-based pre-release version
(e.g. `1.2.3-alpha.0.5`).

The [`Build, Test & Publish`](.github/workflows/publish-packages.yml) workflow builds and tests on
every PR and push to the default branch. It packs and pushes all packages to GitHub Packages only on a
`v*.*.*` tag (or a manual `workflow_dispatch` with `force_publish: true`). `packages:write` is
scoped to the publish job, so PR builds can never push packages.

To cut a release:

```bash
git tag v1.2.3
git push origin v1.2.3
```

## Code quality

Solution-wide analyzers run on every build (see [`Directory.Build.props`](Directory.Build.props)):

- **VSTHRD200** — async methods must end in `Async`.
- **RS0030** — bans static `Serilog.Log` (banned symbols listed in [`BannedSymbols.txt`](BannedSymbols.txt)).

## Changelog

See [CHANGELOG.md](CHANGELOG.md).

## License

MIT.
