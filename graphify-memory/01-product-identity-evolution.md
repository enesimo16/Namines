# Namines — Product Identity & Phase Evolution

## What Namines is

Namines is an AI-assisted database-schema design tool. Flow: natural language / URL / image / voice input → normalized 3NF schema → interactive React Flow canvas → DDL generation for 6 engines (PostgreSQL, SQL Server, MySQL, MariaDB, SQLite, Oracle) → EF Core models, Prisma export, migrations, mock data, docs, diagrams, Docker sandbox, Streamlit admin-app export.

- Backend: .NET 8, split into `Namines.API`, `Namines.Core`, `Namines.Infrastructure`.
- Frontend: Next.js 16 / React 19 / Zustand / React Flow.
- AI providers: Groq (default), Gemini, Ollama, OpenAI (BYOK).
- Auth: JWT in httpOnly cookie.
- Control DB: PostgreSQL (migrated from SQLite during checklist gate G7).

## Phase progression

### new-phase ("Faz 2" master plan, docs new-phase/00–36, dated 2026-08-08 initially)

A 36-document, ~12-month plan to evolve Namines from an "ERD tool" into an "AI-native Data Platform." Introduces a three-Plane architecture — Design Plane, Data Plane, App Plane — unifying schema design (Namines Studio), live databases (Data Plane / Neon), and an auto-generated admin panel + API (Namines Console + Namines Gateway), all derived from one source of truth: NSL (Namines Schema Language).

Tagline: "Change the schema. Everything else updates itself." (TR: "Şema değişir, geri kalan her şey kendini günceller.")

Scope: 141 total features planned — 38 preserved from Phase 1, 21 promoted, 82 entirely new.

### Lifecycle Pivot (2026-08-10, doc 27-LIFECYCLE-PIVOT.md)

A mid-plan strategic correction triggered by real competitive research (updated 01-MARKET.md, 2026-08):

- Supabase shipped visual schema design + declarative diff + Index Advisor + an MCP server.
- "Admin Pilot" already does AI-driven auto-admin-panel generation — the core idea behind Namines Console.
- ChartDB reached 22k+ GitHub stars as a free schema-diagramming tool.
- Lovable / Bolt.new / v0 / Replit Agent commoditized "prompt → app" generation entirely.

Conclusion: the window to "define a new broad-platform category" is closing — Supabase's ecosystem will fill it with resources a solo developer cannot match.

Namines re-narrows its thesis from **"AI ile database üret" (generation-first)** to **"AI ile database/backend lifecycle'ını GÜVENLE yönet" (evolution + governance-first)**.

New north-star, promoted ahead of the broad Console/Gateway expansion:
1. Impact Analysis Engine (doc 28)
2. Database Change Review (doc 29, described as "the database's GitHub PR")
3. Server-Side Branching (doc 30)

New positioning: "AI'ın önerdiği değişiklik kanıtlanabilir şekilde güvenli ve teknik olmayan biri tarafından onaylanabilir" — i.e., not "AI writes your schema" (a claim already commoditized by Claude Code/Cursor), but "prove the change is safe," which coding agents do not do.

**Pivot markers (edges to note):** doc 00-VISION.md points to 27; doc 01-MARKET.md points to 27; docs 02-PRODUCT-SCOPE.md and 24-ROADMAP.md both carry "resequenced by 27" banners. Doc 27 supersedes the *sequencing*, not the *content*, of 00/01/02/24.

### Deferred-Not-Rejected (doc 32-DEFERRED-NOT-REJECTED.md)

Explicitly rejected-for-now ideas, each with a why-not-now / when-yes / cheap-version-today triple:

1. **Generic web/mobile/PWA app builder** — rejected because Lovable/Bolt/v0/Replit Agent already dominate that space. Cheap version today: Console Eject → PWA.
2. **Generic automation platform ("Namines Flow")** — rejected vs. n8n/Zapier/Make. Cheap version: webhook/event source only.
3. **AI Dataset Factory ("Namines Data")** — probably never; wrong buyer persona.
4. **"Database Doctor" performance-improvement claims (e.g. "%X faster")** — rejected as a *correctness* issue, not a feature rejection: no real query telemetry exists yet. OK once Data Plane + `pg_stat_statements` exist.
5. **Running Namines' own PostgreSQL/Kubernetes cluster** — rejected until roughly $8-15K MRR justifies hiring a platform engineer.

### second-phase (opened 2026-08-25)

Faz 1 (new-phase) is declared "done" — CHECKLIST gates G0 through G52 complete, 1136 tests green at close. This folder records post-launch, product-reality-driven decisions layered on a *working* system rather than a forward plan.

Key documents:
- `00-NEREDEYIZ.md` — state-of-the-union snapshot.
- `01-SIRADAKI-ISLER.md` — prioritized backlog.
- `02-REDIS-KARARI.md` — the decision to defer adopting Redis.
- `03-PAZAR-VE-TASARIM-ANALIZI.md` — a harsh self-critique of market position and UI, measured against the actually-running app.
- Numbered work items `04`–`13` and `16`–`17` (loading screen, plan mode, data sources, engine conversion, prompt UX, schema alternatives, multi-DB workspace, code→schema extraction, integrations, shared-hosting export, quota/cost hardening, first-contact/pricing) — nearly all marked done with live-verification notes.

### third-phase (doc 00-BASLA-BURADAN.md)

Introduces three **separate microservices**:
- **Namines Ground** — a managed live database (Supabase-like), not started.
- **Namines Vault** — real database backup (`.bak`/`pg_dump`/`mysqldump`), not started.
- **Namines Desk** — a hosted, deterministic CRUD admin panel (`namines.com/<user>/<project>`), auto-generated from DB schema introspection, actively being built.

Order chosen deliberately: **Desk → Vault → Ground**, because Desk's backend was already 100% proven (see 02-architecture doc), Vault has code but backs up the wrong thing, and Ground would require the largest new infrastructure investment (provisioning, quota, isolation, backup, on-call) with the highest risk (data loss ends the product).

Desk status: v0.1 shipped (deterministic CRUD, no AI) and verified end-to-end against a real PostgreSQL database; v1 planning moved into a dedicated `namines_desk/` folder with one document per screen (00 through 09).

Key facts already established for Desk:
- Backend proven live: list/create/update/delete/introspect endpoints all verified against real Postgres, including independent verification via direct `psql` queries (not trusting the API's own claims).
- A real bug was found and fixed: FK relations were never populated by `dbintrospect` — fixed for PostgreSQL via `pg_catalog` (not `information_schema`, because composite-FK column-pair ordering isn't guaranteed there); other engines (MSSQL/MySQL/MariaDB/Oracle/SQLite) still lack this fix.
- Connection-string encryption: AES-256-GCM via a new `IConnectionSecretProtector` abstraction, because Desk is a *hosted* panel (unlike the design tool, which never stored connection strings).
- Desk shipped as a fully separate Next.js app: own port (3200), own `package.json`, zero code references to `Namines.Core` or the main `frontend/`.
- Old Streamlit/scaffolder "download hub panel" UI removed from the compile screen in favor of a hosted Desk link.
- "Push schema changes directly to the live DB" is explicitly deferred until Namines Vault exists, because `ALTER TABLE` is irreversible without a pre-migration backup.
