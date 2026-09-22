# Namines — Architecture

## Control plane vs data plane vs app plane (new-phase docs 03, 05, 06, 26-GLOSSARY.md)

- **Control Plane** — metadata only: org / user / project / schema-version / branch / billing / API-keys. Never touches tenant rows. Served by `namines-api` (.NET, planned migration path .NET 8 → 9 → 10 LTS).
- **Data Plane** — real tenant databases, four modes:
  - Ephemeral (sandbox, TTL 60 minutes)
  - Managed (Neon-provisioned)
  - Branch (copy-on-write, via Neon)
  - BYODB (bring-your-own-database, via `DbConnectionPanel` + `SsrfGuard`)
  - Abstracted behind `IDatabaseProvider`, with implementations `NeonProvider`, `PlanetScaleProvider`, `AzureSqlProvider`, `EphemeralK8sProvider`, `SelfHostedPgProvider`.
- **App Plane** — Namines Console (auto-generated admin UI, runtime-rendered, NOT codegen — ADR-01) + Namines Gateway (auto-generated REST/GraphQL API).

**ADR-05 (repeated everywhere as a hard rule):** `docker.sock` is NEVER mounted into a container — it is host-root-equivalent privilege and unacceptable in a multi-tenant SaaS. The ephemeral sandbox instead uses the Kubernetes Job API with the gVisor runtime class.

## NSL — Namines Schema Language (doc 04-NSL-SCHEMA-IR.md)

The intended single source of truth / intermediate representation the whole system compiles from. Format: a text `.nsl` form plus a canonical JSON IR plus a JSON Schema (`packages/nsl-spec/nsl-1.0.schema.json`).

Design goals: expressiveness (index / unique / check / composite-key / enum / view / RLS / partition / collation / generated columns), engine-independence, human-readable and git-diffable, stable UUID identity per object (so renames don't break diffs), a pure/no-I/O parser (so it can be compiled to WASM).

NSL directly fixes Phase-1's biggest structural flaw: the old `DatabaseSchema` model had no index/unique/check support and hard-coded `ON DELETE CASCADE` on every foreign key — this was the root cause of SQL Server rejecting schemas with "multiple cascade paths" (Msg 1785). The new default is `onDelete: "no_action"`, and `FkCascadeAnalyzer` validates cascade paths before compilation.

## Gateway API (doc 08-GATEWAY-API.md)

Auto-generated REST + GraphQL over the tenant database, using a PostgREST/Supabase-like filter syntax, e.g. `?status=in.(paid,shipped)&total=gte.100&expand=user`. OpenAPI 3.1 and GraphQL SDL are auto-generated. Two-layer authorization: app-level role/column filtering plus DB-level Row-Level Security (RLS). API keys use the `nam_live_...` prefix, originally hashed with argon2id, later changed to SHA-256 in the shipped implementation.

Actually implemented (per CHECKLIST gates G14, G23, G26–G29, G41, G48): `list` / `detail` / `create` / `update` / `delete` / `import` / `rpc` / `query` / `query-nl`, key-scoped table permissions, per-key rate limits, a write-only audit log (`GatewayAuditEntry`), CSV export.

## Console (doc 07-CONSOLE-ADMIN-UI.md)

Described as "the most important new product" in the new-phase plan. Runtime-rendered (not codegen) admin panel: a single Next.js app serves all projects via a metadata contract. Includes a column→widget mapping table, table→page-pattern auto-selection (CRUD / master-detail / tree / kanban / singleton / junction-as-relation-editor), Console-specific RBAC distinct from org-level RBAC, an audit log, and **Console Eject** — the ability to export the generated panel to Next.js, React, Blazor, or Streamlit (Streamlit here is literally the Phase-1 feature reincarnated as an eject target). Shipped per CHECKLIST: G31 (Next.js eject panel), G40 (write screens), G47 (RBAC + audit).

## Why third-phase splits into three separate microservices

Namines Ground, Namines Vault, and Namines Desk are each their own microservice with their own `.sln`/`package.json`, own `docker-compose.yml`, own migrations, own tests, own port. The design rule (from `namines_desk/00-GENEL-BAKIS.md` §6 and `services/desk/README.md`) is deliberate: Ground/Vault/Desk serve a different user than the design tool (operator vs. designer), have a different session model, and deploy on a different cadence.

Concretely for Desk: its own `package.json`, its own port (3200), its own `node_modules`, **zero code references** to `frontend/` or `Namines.Core`. All communication is HTTP-only, via `/api/gateway/*`. Types like `DeskTable` are **intentionally duplicated** rather than shared through a common package.

The explicit rule stated in the docs: **"Bu kural olmasaydı bu, klasörü ayrılmış tek bir monolit olurdu"** — "without this rule, this would just be a monolith with separated folders." The only sanctioned exception: if a shared type truly becomes necessary, either publish a small contracts package or deliberately copy it — never take a project reference to `Namines.Core`.

## MCP server architecture decision (doc 33-MCP-AND-SKILL.md §4)

Two options were considered for exposing Namines to AI coding agents via MCP:
- (a) A Node/TS MCP server that calls the hosted backend over HTTP.
- (b) A .NET MCP server that embeds `Namines.Core`/`Namines.Infrastructure` directly and runs on the user's own machine.

**Chosen: (b).** Option (a) was rejected because it reintroduces the SSRF problem the hosted backend already has to guard against (a hosted service still cannot safely reach a user's `localhost` database), adds authentication friction, and would push Docker execution cost for the `run_tests` tool onto the vendor. Running the MCP server locally lets it reach `localhost:5432` directly — a path the hosted product's `SsrfGuard` deliberately blocks.

A related hard rule: **the MCP server's `namines_open_change_request` tool is the only MCP tool that writes anywhere, and it writes to the Namines server (opening a change review), never to the user's own database.** This is a deliberate architectural firewall.
