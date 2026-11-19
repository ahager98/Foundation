# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Foundation is a set of shared foundational libraries for multi-tenant microservices. Each
project under `src/` ships as an **independent `Foundation.*` NuGet package** so a service depends on only
the cross-cutting pieces it needs. There is no application host here — every project is a library, and
the "consumer" is always a separate service repo. Targets **.NET 10** (nullable + implicit usings
solution-wide).

## Build / test commands

```bash
dotnet restore Foundation.slnx
dotnet build   Foundation.slnx -c Release
dotnet test    Foundation.slnx -c Release
```

- Tests use **MSTest**. Each `src/<Package>` has a matching `tests/<Package>.Tests` project.
- Run one test project: `dotnet test tests/Foundation.Persistence.Tests/Foundation.Persistence.Tests.csproj`
- Run a single test / subset: append `--filter "FullyQualifiedName~<name>"`.
- Coverage (as CI collects it): add `--collect:"XPlat Code Coverage"`.

## NuGet feed auth (needed for restore)

`nuget.config` adds the `github-ahager98` GitHub Packages feed and uses `packageSourceMapping` so
**only `Foundation.*` packages** resolve from GitHub; everything else comes from nuget.org. Restore requires
`GITHUB_ACTOR` and `GITHUB_TOKEN` (a PAT with `read:packages`) set in the environment — the config reads
them as `%GITHUB_ACTOR%` / `%GITHUB_TOKEN%`. Never hard-code credentials in the committed config.

## Code-quality analyzers (enforced on every build)

The coding standard is `CODE_QUALITY_STANDARD.md` (repo root). Read it before
writing code (for example: no `var`, explicit types). The analyzers below enforce only part of it.

Configured in `Directory.Build.props` (`TreatWarningsAsErrors` is off, but honor these):

- **VSTHRD200** — async methods must end in `Async`.
- **RS0030** — static `Serilog.Log` is banned (see `BannedSymbols.txt`); use injected loggers.

## Architecture: the two orthogonal axes in persistence

The single most important design idea to internalize is in `Foundation.Persistence`. A service selects
**two independent things from configuration** — the same binaries deploy either way, with no code change:

1. **Tenancy strategy** (`Tenancy:Strategy`): `DatabasePerTenant` (default; one physical DB per tenant,
   isolated at the connection level via a `{tenantId}` placeholder in the connection string) vs
   `SharedDatabase` (one DB, rows isolated by a `TenantId` column and an EF global query filter).
2. **Database provider** (`Database:Provider`): `SqlServer` (default) vs `Postgres`.

`AddFoundationPersistence<TContext>(configuration)` is the **single registration entry point** for both axes —
it binds tenancy options, picks the matching `ITenantConnectionStringBuilder`, and registers `TContext`
on the matching EF provider. Derive the context from `TenantDbContext`.

### Non-obvious invariants (these bite if unknown)

- **Query-filter ordering.** `TenantDbContext` uses EF Core 10 *named* query filters. A derived
  context that overrides `OnModelCreating` **must apply its own filters (e.g. soft-delete `e.IsActive`)
  first and call `base.OnModelCreating(modelBuilder)` LAST** — the base lifts the derived anonymous
  filter to a named one and adds the named tenant filter. `IgnoreQueryFilters()` with no args drops
  *both*; pass `[TenantDbContext.SoftDeleteQueryFilterName]` to drop only soft-delete while keeping
  tenant isolation.
- **Opt-in tenant scoping.** Only entities marked `ITenantScoped` get the shared-DB tenant filter +
  `TenantId` stamping. Tenant-registry entities and cross-tenant/global entities (users, global lookups,
  membership joins) must stay **unmarked** so they remain queryable when no tenant is resolved.
- **Fail-closed tenancy.** An unresolved tenant id is `0`, which matches no rows; cross-tenant writes are
  rejected at `SaveChanges`. When seeding `ITenantScoped` children in the request that creates a tenant,
  either leave `TenantId == 0` (the context stamps it) or call `SetTenantId(newId)` first.
- **Provider-portable concurrency.** `BaseEntity.RowVersion` is SQL-Server-shaped. Call
  `modelBuilder.ApplyFoundationConcurrencyTokens(Database)` in `OnModelCreating` instead of hardcoding
  `IsRowVersion()` — it maps `rowversion` on SQL Server, uses system `xmin` on Postgres, no-ops in-memory.
- **Audit timestamps** are stamped by `TenantDbContext` on save (both dates on insert, `UpdatedDate`
  on update; `CreatedDate` never rewritten) via a `protected virtual TimeProvider Clock` seam. User-id
  audit fields (`CreatedById`/`UpdatedById`) remain the caller's responsibility.

- **Safe updates.** Update through `db.UpdateTrackedAsync<T>(id, apply, expectedRowVersion, updatedById)` (`TrackedUpdateExtensions`), not by mapping a model to a new entity. It loads the tracked row (tenant-filtered; another tenant's row is `ResourceNotFoundException`), rejects a stale `RowVersion` with `ConcurrencyConflictException`, and throws if the delegate changes `Id`/`TenantId`/`CreatedDate`/`CreatedById`. Edit child collections inside it with `TrackedChildren.Sync` so existing children keep their keys, and pass `include: q => q.Include(p => p.Children)` so the persisted children are loaded (without it every incoming child is added as new). `expectedRowVersion` and `updatedById` are required (pass `null` for the token to skip the caller's If-Match check; the store still rejects a writer that slips in between load and save); `scope:` restricts the lookup to the caller's sub-scope and `audit:` runs just before the save so an audit row added to the same context commits atomically with the update. On a relational provider it throws unless `RowVersion` is mapped as a concurrency token (`ApplyFoundationConcurrencyTokens`).

- **HIPAA check.** `Foundation.Compliance` holds `[Phi]`/`[Pii]`/`[NotClassified(reason)]`/`[Retention]`, the BAA inventory and `HipaaCheck.Run(options)`. A service calls it from a test with its assemblies, source files, appsettings files and `BaaInventory`, fails on `!report.Passed`, and attaches `report.ToMarkdown()` to the PR. Rules: unclassified fields on person/case/note/form types (the type-name pattern is generic by default; widen it via `SensitiveTypePattern`, and add service scoping columns via `ExemptMemberNames`), missing retention, endpoints touching classified types without `[Authorize(Policy|Roles)]`, classified members in log statements, egress hosts not in the inventory, insecure config, expired waivers. It is a technical guardrail, not a HIPAA certification; the log and config scans are heuristics, waive false positives. Known gaps: the log scan misses static `Serilog.Log`, `{@obj}` destructuring and statements containing a `;` in a string; the config scan misses secrets inside connection strings and `https://user@host`; the authorization scan checks signature types only, not nested DTO properties.

- **Write-through cache.** Cache an aggregate with `WriteThroughCache<T>` (`AddFoundationWriteThroughCache<T>` plus an `ICacheScopeProvider`): reads via `GetOrCreateAsync`, every write via `WriteAsync`, which evicts the whole tenant (every sub-scope) before and after the write, even if the write throws. Keys carry tenant and sub-scope (`CacheScope.SubScopeId`), must be plain identifiers, are bounded per tenant-and-sub-scope scope plus a cap on tracked scopes (per process; worst case their product, so lower them for tenants with many sub-scopes), and an unresolved tenant bypasses the cache. `ContainsClassifiedData` defaults to true (TTL capped at 5 minutes, L1 capped at 1 minute); set it false only for an aggregate known to hold neither PHI nor PII. An eviction failure never replaces the write's result or exception; it goes to `OnEvictionFailed` and stale entries live at most the TTL.

- **Idempotent handlers.** Derive from `IdempotentEventHandler<TItem>` (`Foundation.Idempotency`) and implement only `ProcessOnceAsync`. `HandleAsync(eventId, tenantId, items)` claims each item before any side effect, completes or releases the claim, and isolates failures per item. Failures reach the `OnItemFailed(eventId, itemKey, exception)` hook (never `ItemResult`); completion is retried, and `CompleteAsync`/`ReleaseAsync` return whether the caller still owned the claim (a taken-over claim is reported as `ClaimLost`). Call `EfIdempotencyClaimStore.PurgeExpiredAsync` from a periodic sweep (batched; once per tenant database under DatabasePerTenant). Items run sequentially: each costs a claim and a completion round-trip. For production register `EfIdempotencyClaimStore<TContext>` (it needs an `IDbContextFactory<TContext>`, which `AddFoundationPersistence` does not register; add `AddDbContextFactory` with the same provider and a scoped lifetime so the tenant resolves; the factory context's tenant must match the tenant you pass to `HandleAsync`), call `modelBuilder.ApplyFoundationIdempotencyClaims()` and generate the migration; `InMemoryIdempotencyClaimStore` is single-process only. Keep the claim state out of your own pending-select status column, and give outbound providers an idempotency key (event id + item key): delivery is still at-least-once if a worker crashes after a side effect.

- **C-CDA parsing.** `Foundation.Ccda` is dependency-free (`System.Xml.Linq` only); `CcdaParser.Parse`/`TryParse`/`ParseAsync` return a `CcdaDocument` with entries flattened to document level. Keep input handling fail-closed: DTDs stay prohibited with no `XmlResolver`, every size/depth/count stays capped in `CcdaParserOptions`, the iterative depth check runs before any recursive narrative rendering, and `CcdaParseException` carries only a reason plus line/position (never the `XmlException`, whose message can quote PHI). Model types are plain classes, not records, so an accidental `ToString()` in a log prints no PHI. Entry readers are tolerant: an unexpected shape is skipped, not thrown. New entry types are classified in `CcdaClassifier` and read in `CcdaEntryReader`.

### What consuming services still own

- **EF migrations live in each service, not here.** They are provider-specific; generate them against the
  provider that service deploys with (support both via separate `Migrations/SqlServer` + `Migrations/Postgres`
  sets). Shared-DB deployments need the `TenantId` column + index on each `ITenantScoped` table — generate
  under `SharedDatabase` so the single set also serves `DatabasePerTenant` (column carried unused).
- Provider-specific column types (`datetime2`, `nvarchar`, computed/JSON) must be conditioned on the
  active provider in `OnModelCreating`.

## What belongs in this repo

A shared versioned package earns its place only when it is used by **two or more services** and pays
for itself in not being re-derived. The infrastructure projects (`DataAccess`, `Persistence`,
`Tenancy`, `Idempotency`, `Caching`, `Compliance`) pass that test. Gateway routing, ASP.NET Core
pipeline wiring, time-zone handling and message-bus contracts were removed from this repo because each
belongs with the single service or product that owns it. A library only one service consumes is a
folder, not a package.

## Versioning & release

Tag-driven via **MinVer** (`MinVerTagPrefix = v`): a `vX.Y.Z` git tag sets the version for every package;
untagged builds get a height-based pre-release. The `publish-packages.yml` workflow builds + tests on
every PR/push, and packs + pushes all packages to GitHub Packages **only on a `v*.*.*` tag** (or manual
dispatch with `force_publish: true`); `packages:write` is scoped to the publish job so PR builds can never
push. Cut a release by pushing a tag:

```bash
git tag v1.2.3 && git push origin v1.2.3
```

Keep the Changelog section of `README.md` in step with each new tag.
