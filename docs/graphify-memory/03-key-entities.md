# Namines — Key Named Entities

## Core domain/services (backend)

`Namines.API`, `Namines.Core`, `Namines.Infrastructure`, `Namines.Tests`, `Namines.Tests.RunTests`, `Namines.Mcp`, `Namines.Cli` (planned), `AuthDbContext`, `CloudProject` (fields: `SchemaJson`, `NodePositionsJson`, `EncryptedConnectionString`, `AutoApproveSafeChanges`, `UserId`), `ApplicationUser`, `Branch` / `SchemaVersion` (control-DB tables, added at gate G10), `ChangeRequest` / `ChangeRequestApproval` / `ChangeRequestAuditLog` (added at gates G11/G16), `GatewayApiKey`, `GatewayAuditEntry` (a write-only audit log — records only Create/Update/Delete/Import/Rpc/Sql, never reads — with a `Kind` enum and a `ProjectId` field), `UsageEvent` (user-scoped, **has no `ProjectId`** — a documented gap that blocks per-project billing), `CrossDatabaseRelation` (from second-phase doc 10), `PlanQuotas`, `PricingCatalog`, `NaiCatalog` (the model-cost catalog behind the NAI naming abstraction).

## Analyzers/engines (pure, deterministic — a repeated architectural pattern)

- `FkCascadeAnalyzer` (gate G3) was later generalized (not rewritten) into `SchemaImpactAnalyzer` (gate G8), which produces an `ImpactReport`. That report feeds both `ChangeRequestApprovalPolicy` (gate G11) and the `MigrationService`'s risk classification (gate G9).
- `AffectedCodeScanner` (gate G13).
- `GatewayService` / `GatewayController` (gate G14 minimal read-only Gateway, later expanded to full CRUD).
- `EngineConversionAnalyzer` + `SchemaConverter` (second-phase doc 07 — cross-engine conversion loss report).
- `CrossDatabaseImpactAnalyzer` (second-phase doc 10).
- `CodeSchemaExtractor`, composed of `PrismaSchemaParser`, `EfCoreEntityParser`, `SqlDdlSchemaParser` (second-phase doc 11 — extracting schema from existing code instead of a live DB).
- `ApiSpecExtractor` (GraphQL/OpenAPI introspection; replaced an earlier HTML-scraping `ReferenceUrl` approach, second-phase doc 06).
- `JsonShapeInferencer` (second-phase doc 06, tier 3 of the data-source extraction ladder).
- `PlanBuilder` (second-phase doc 05, Plan Mode — clarify before generating).
- `SchemaAgentPipeline` (draft → lint → DDL-compile → repair loop, described in doc 36-KOTA-VE-AJAN.md).
- `SharedHostingExporter` / `SqlFileSplitter` / `SqliteFileBuilder` (second-phase doc 13 — export for shared-hosting environments).

## Security/infra primitives

- `SsrfGuard` — blocks the app from reaching loopback/private IP ranges when the user supplies a "bring your own database" connection string; this is the exact protection the local MCP server design (see architecture doc) has to route around by running on the user's own machine instead.
- `IConnectionSecretProtector` — AES-256-GCM connection-string encryption, introduced specifically for Namines Desk in third-phase.
- `ReferentialActionSql` — encodes the rule "never silently degrade a referential action toward CASCADE."
- `IPresenceStore` / `InMemoryPresenceStore` / `RedisPresenceStore` (gate G6 — SignalR presence, Redis-pluggable by design from day one).
- `GatewayRateLimiter` (in-memory today, designed to be swapped for a Redis-backed implementation later).
- `PiiRedactionEnricher` (log scrubbing for observability).
- `EnvironmentValidator`.

## Third-phase / Desk-specific

Namines Ground, Namines Vault, Namines Desk (`services/desk/`, port 3200), the `DeskTable` type (intentionally duplicated rather than shared), the `dbintrospect` FK-relations bug (fixed for PostgreSQL only, via `pg_catalog`), `ResolveConnectionAsync` (the Gateway's connection-resolution chain — the exact place where a new "resolve via authenticated session + `projectId`" branch has to be added to support Desk's JWT-only auth model), `GET /api/gateway/schema`, `PUT /api/gateway/keys/project/{id}/connection`, `AuditTrailAsync` (will need pagination/date-range support to power Desk's Logs screen).

## NSL / compiler ecosystem

NSL (Namines Schema Language), the canonical JSON IR (shipped as `ir.json` at gate G46), `Namines.Nsl`, `Namines.Compiler`, `NslParser` / `NslWriter` / `NslValidator` / `NslDiffer` / `MigrationPlanner` / `NslMerger`, per-engine DDL backends (`ddl.postgres` … `ddl.oracle`), ORM backends — EF Core and Prisma are shipped; Drizzle, TypeORM, SQLAlchemy, and Django are still only planned.

## Named products / brand architecture (doc 00-VISION.md §4 — mostly aspirational, new-phase level)

Namines (umbrella brand), Namines Studio, Namines Copilot, NSL, Namines Cloud, **Namines Console**, **Namines Gateway**, Namines CLI, **Namines Bot** (a GitHub App — code complete at gate G43, but blocked on GitHub App credentials existing), Namines Bridge (an on-prem agent, priority tier P2, not built), Namines Hub (a blueprint marketplace), Namines Docs, Namines Status.

Third-phase introduces **Namines Ground**, **Namines Vault**, **Namines Desk** as separate services sitting outside this original brand map entirely.

## Test/quality infrastructure

Golden-file tests using `Verify.Xunit` (fixtures under `Golden/{Engine}/*.verified.sql`), Testcontainers-based integration tests (gate G5), `Namines.Tests.RunTests` (a project deliberately isolated from `Namines.Tests` — see the Testcontainers DLL-collision entry in the relationships file), an AI evaluation harness (`packages/evals`, planned at the new-phase level but not yet built), and `check:templates` / `check:design` / `check:e2e` scripts — these ARE shipped and are referenced repeatedly throughout the docs as live regression gates that catch real bugs before users do.
