<div align="center">

# Namines

### AI-assisted database design, from a plain-language description to a running, backed-up database.

**English** · [Türkçe](README.tr.md)

</div>

---

## Table of contents

- [What this is](#what-this-is)
- [Who this is for](#who-this-is-for)
- [How it fits together](#how-it-fits-together)
- [Screenshots](#screenshots)
- [Features](#features)
- [Tech stack](#tech-stack)
- [Project structure](#project-structure)
- [Getting started](#getting-started)
- [AI token model](#ai-token-model)
- [Security](#security)
- [License](#license)

## What this is

Namines is a database design and lifecycle tool. It takes a plain-language description of an
application (or an existing schema, image, or reference URL) and produces a normalized schema,
an editable visual canvas, and generated artifacts — DDL, EF Core models, migrations, mock data,
diagrams, documentation — across six database engines.

Beyond design, Namines can also open a real, hosted database for a project (**Ground**), apply the
generated schema to it, take a first backup (**Vault**), and hand back a working data-management
panel (**Desk**) — all from one action in the compile screen, called **Launch**. Changes to a
database that already has data are routed through a change-review flow instead of being applied
silently.

## Who this is for

Developers and small teams who want to go from an idea to a working, inspectable database schema
without hand-writing DDL or migrations first, and who want a lightweight way to host, back up, and
browse that database without standing up separate infrastructure for it.

## How it fits together

| Piece | What it does |
|---|---|
| Canvas (`/canvas`) | Generate or hand-build a schema; edit it visually, collaboratively, with version history. |
| Compile (`/compile`) | Turn the schema into DDL / EF Core / Prisma / mock data / docs / diagrams for a chosen engine. |
| Launch | From `/compile`, provision a database (Ground), apply the schema, take a first backup (Vault), and get a Desk link — one action, or routes to review if the target already has data. |
| Ground (`/ground`) | Hosted database provisioning and status, independent of Desk. |
| Vault (`/vault`) | Scheduled and manual backups, restore, independent of Desk. |
| Desk (`services/desk`, separate app) | A CRUD/SQL data-management panel for a project's live database. |

## Screenshots

The screenshots previously here were captured before this session's interface rewrite (English UI,
new visual style, and the Ground/Vault/Launch features above) and no longer reflect the current
application. They have been removed rather than left showing a different product. Run the app
locally (see [Getting started](#getting-started)) to see the current UI.

## Features

### AI-powered design
- Schema generation from natural language, a reference URL, or an image (vision).
- Voice input for the generation prompt (Whisper).
- AI DBA advisor: a schema health score with prioritized, explained issues.
- Smart Seed: domain-aware mock/test data.
- Reverse engineering: turn an existing `DbContext` back into a visual schema.

### Visual workspace
- Interactive canvas (React Flow): drag-and-drop tables, columns, and relations.
- Command palette (Ctrl/Cmd+K) for any action from the keyboard.
- Undo/redo with a 50-snapshot history stack.
- Canvas search (Ctrl+F) to find and zoom to any table or column.
- Five pre-built starter schemas (e-commerce, SaaS, CRM, blog, healthcare); merge into or replace
  the current schema.
- Real-time collaboration: shareable rooms with live cursors and schema sync (SignalR).
- Version control: branches and a per-workspace migration baseline.

### Compilation and export
- Multi-engine DDL: SQL Server, PostgreSQL, MySQL, MariaDB, SQLite, Oracle.
- EF Core models and a guided migration wizard (diff and preview).
- Prisma schema export.
- SQL DDL import: paste or upload a `.sql` file (`CREATE TABLE`, `ALTER TABLE ADD/DROP COLUMN`,
  `ALTER TABLE ADD FOREIGN KEY`).
- In-browser SQL console: run the generated DDL locally via SQLite (WASM).
- Docker sandbox: provision a throwaway DB container and download a backup.
- Downloadable code project: a scaffolded full-stack project that talks to the database through a
  scoped, revocable Gateway API key — never a raw connection string.
- Documentation and diagrams: Data Dictionary PDF, README.md, Mermaid ER/class/flow.

### Launch, Ground, Vault, Desk
- Launch: one action from `/compile` that provisions a database, applies the schema, takes a
  first backup, and returns a link into Desk — or, if the target database already has data, routes
  the change to the existing review flow instead of touching it.
- Ground: hosted PostgreSQL provisioning (local, Neon, or Supabase, depending on configuration),
  independent of Desk.
- Vault: scheduled and manual backups with restore, independent of Desk.
- Desk: a separate application (`services/desk`) providing CRUD, a SQL console, API keys, and
  deployment/logs views for a project's live database.

### CI / developer tooling
- Schema diff (`scripts/namines-diff.mjs`): a dependency-free Markdown diff report between two
  schema JSON files. Detects added/dropped tables, columns, and relations; exits with status 2 on
  destructive changes so CI can gate merges.

### Platform
- Accounts and cloud sync: JWT in an httpOnly cookie; projects saved to the cloud (branches are
  kept locally, per device).
- Fair AI usage: a shared daily token pool with a per-user cap; on exhaustion, supported features
  fall back to a free local engine instead of blocking.
- Pro plan: optional paid tier via Stripe Hosted Checkout.
- Feedback widget on the homepage.

## Tech stack

| Layer | Stack |
|---|---|
| Frontend | Next.js 16, React 19, TypeScript, Zustand, React Flow, Tailwind CSS |
| Desk | Next.js 16, TypeScript, Tailwind CSS (separate app under `services/desk`) |
| Backend | .NET 8, ASP.NET Core, EF Core (PostgreSQL), SignalR (Redis backplane), Serilog |
| AI | Groq (Llama 3.3 70B / GPT-OSS 120B / Llama 4 Scout), Google Gemini, Ollama, OpenAI (BYOK), Whisper |
| Infra | Docker / docker-compose, Stripe |

## Project structure

```
backend/
  Namines.API/             ASP.NET Core Web API (controllers, middleware, SignalR hub)
  Namines.Core/            Domain models, prompt builders, interfaces, shared utilities
  Namines.Infrastructure/  AI services, DDL generators, EF Core, data access, Launch/Ground/Vault services
  Namines.Ground/          Database provisioning providers (LocalPostgres, Neon, Supabase)
  Namines.Vault/           Backup providers and storage
frontend/                  Next.js app (canvas, compile, Ground/Vault pages, stores, hooks)
services/desk/             Separate Next.js app: CRUD/SQL panel for a project's live database
docker-compose.yml         Control DB + backend + frontend containers
```

## Getting started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/) and [Node.js 20+](https://nodejs.org/)
- A free [Groq API key](https://console.groq.com/keys)
- Docker Desktop — required for the control database (PostgreSQL) and for Ground/Vault/Launch;
  optional if you only need schema design and compilation without a live database.

### 1. Configure secrets
All secrets live in a single git-ignored `.env` at the repo root:

```bash
cp .env.example .env
```

Fill in at least `Jwt__Key` (32+ chars) and `Groq__ApiKey`. The `__` separator maps to .NET config
(`Jwt__Key` → `Jwt:Key`); the backend loads `.env` automatically on startup.

### 2. Start the control database
```bash
docker compose up -d namines-control-db
```

### 3. Run the backend
```bash
cd backend/Namines.API
dotnet run
# http://localhost:5000  (Swagger at /swagger)
```

### 4. Run the frontend
```bash
cd frontend
npm install
npm run dev
# http://localhost:3000
```

### 5. (Optional) Run Desk
```bash
cd services/desk
npm install
npm run dev
# http://localhost:3200
```

### ...or run everything with Docker
```bash
docker compose up --build
```
(This starts the control database, backend, and frontend. Desk is started separately, as above.)

## Deploying

Production uses separate env files, not the local `.env`:

| Target | Template | Notes |
|---|---|---|
| Backend (Railway / Render / Fly / VPS) | [`deploy/backend.env.example`](deploy/backend.env.example) | `Jwt__Key`, `Groq__ApiKey`, `App__FrontendUrl` are required |
| Frontend (Vercel) | [`deploy/frontend.env.example`](deploy/frontend.env.example) | Only `NEXT_PUBLIC_API_URL`, baked in at build time |

Three settings decide whether a deploy works:

1. `App__FrontendUrl` must equal the frontend's exact origin. In Production, localhost is not
   auto-allowed, so a wrong value blocks every browser request via CORS.
2. `Auth__CrossSiteCookie=true` is required when frontend and API are on different sites (for
   example Vercel + Railway). Otherwise the browser drops the auth cookie and login never sticks.
3. `NEXT_PUBLIC_API_URL` must not end in `/api` — the client appends it.

The control database is PostgreSQL. Use a managed provider (Neon, RDS, Azure Database) or persist
the `namines-control-db-data` volume; either way, back it up, or every account is wiped on
redeploy.

## AI token model

Namines meters premium AI usage against a shared daily token pool, so a single user cannot drain it
without pre-allocating tokens to dormant accounts:

- `AiPool:DailyTokenPool`: shared daily budget (default 100,000, roughly Groq's free-tier daily
  tokens).
- `AiPool:PerUserDailyTokens`: per-user daily cap (default 20,000).
- Consumption is charged on demand. When the pool or a user's cap is exhausted, supported features
  (docs, mock data, dev package, reverse engineering) fall back to the free local engine instead of
  erroring out.

Raise the pool at any time (for example to 1,000,000) in `appsettings.json`; no code changes
needed.

## Security

- Secrets are never committed; a single git-ignored `.env` is the source of truth.
- JWT is stored in an httpOnly cookie, not `localStorage`, mitigating token theft via XSS.
- BYOK API keys are encrypted at rest with AES-256-GCM (non-extractable Web Crypto key).
- Database connection strings are encrypted at rest (AES-256-GCM) and are never returned to a
  client; a downloaded code project authenticates through a scoped, revocable Gateway API key
  instead.
- SSRF guards on server-side URL fetching and database connection targets, rate limiting on
  sensitive endpoints, and prompt-injection hardening on AI prompts.
- Desk's SSO handoff passes its one-time token in a POST body, never a URL, and is restricted to
  same-site or explicitly trusted origins.

## License

Released under the [MIT License](LICENSE).
