<div align="center">

# ⚡ Namines

**Design interactive database architectures in seconds — with AI.**

Describe your app in plain language and Namines generates a normalized schema, renders it on an
interactive canvas, and produces production‑ready DDL, EF Core models, migrations, mock data,
documentation and more — across six database engines.

![Namines](docs/screenshots/landing.png)

</div>

---

## ✨ Features

- **AI schema generation** — natural‑language, reference‑URL or image (vision) → normalized 3NF schema.
- **Interactive canvas** — drag‑and‑drop tables, relations and columns (React Flow); real‑time
  multiplayer rooms with live cursors (SignalR).
- **Multi‑engine DDL** — SQL Server, PostgreSQL, MySQL, MariaDB, SQLite and Oracle.
- **EF Core scaffolding & migrations** — generate `DbContext`/entities and a guided migration wizard
  (per‑workspace, diff + preview).
- **AI DBA advisor** — schema health score + prioritized issues.
- **Smart Seed** — domain‑aware mock/test data generation.
- **In‑browser SQL console** — run the generated DDL locally via SQLite (sql.js / WASM).
- **Docker sandbox** — spin up a throwaway database container and download a `.bak`.
- **Developer package** — export a ready‑to‑run Streamlit admin app as a ZIP.
- **Docs & diagrams** — Data Dictionary PDF, README.md, Mermaid ER / class / flow diagrams (TR/EN).
- **Reverse engineering** — turn an existing `DbContext` back into a visual schema.
- **Voice input** — dictate your prompt (Whisper).
- **Accounts & cloud sync** — JWT auth over an httpOnly cookie; projects/branches synced to the cloud.
- **Fair AI usage** — a shared daily token pool with a per‑user cap; when exhausted, requests fall
  back to a free local engine instead of blocking (see [AI token model](#-ai-token-model)).
- **Pro plan** — optional $5/mo tier via Stripe Hosted Checkout.
- **Feedback widget** — built‑in bug/idea reporting.

## 🧱 Tech stack

| Layer | Stack |
|---|---|
| Frontend | Next.js 16, React 19, TypeScript, Zustand, React Flow, Tailwind CSS |
| Backend | .NET 8, ASP.NET Core, EF Core (SQLite), SignalR, Serilog |
| AI | Groq (Llama 3.3 70B / GPT‑OSS 120B / Llama 4 Scout), Google Gemini, Ollama, OpenAI (BYOK), Whisper |
| Infra | Docker / docker‑compose, Stripe |

## 🗂️ Structure

```
backend/
  Namines.API/            ASP.NET Core Web API (controllers, middleware, SignalR hub)
  Namines.Core/           Domain models, prompt builders, interfaces
  Namines.Infrastructure/ AI services, DDL generators, EF Core, data access
frontend/                 Next.js app (canvas, compile, panels, stores, hooks)
docker-compose.yml        Backend + frontend containers
```

## 🚀 Getting started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/), [Node.js 20+](https://nodejs.org/)
- A [Groq API key](https://console.groq.com/keys) (free tier works)
- (Optional) Docker Desktop — required only for the Docker sandbox feature

### 1. Configure secrets
All secrets live in a single **git‑ignored** `.env` at the repo root:

```bash
cp .env.example .env
```

Fill in at least `Jwt__Key` (32+ chars) and `Groq__ApiKey`. The `__` separator maps to .NET config
(`Jwt__Key` → `Jwt:Key`). The backend auto‑loads `.env` on startup.

### 2. Run the backend
```bash
cd backend/Namines.API
dotnet run
# → http://localhost:5000  (Swagger at /swagger)
```

### 3. Run the frontend
```bash
cd frontend
npm install
npm run dev
# → http://localhost:3000
```

### Or with Docker
```bash
docker compose up --build
```

## 🎛️ AI token model

Namines meters premium AI usage against a **shared daily token pool** so a single user can't drain it,
without pre‑allocating tokens to dormant accounts:

- `AiPool:DailyTokenPool` — shared daily budget (default **100 000**, ~Groq free‑tier daily tokens).
- `AiPool:PerUserDailyTokens` — per‑user daily cap (default **20 000**).
- Consumption is charged **on demand**; when the pool **or** a user's cap is exhausted, requests
  transparently **fall back to the free local engine** — every free feature keeps working.

Raise the pool at any time (e.g. to `1000000`) in `appsettings.json` — no code changes needed.

## 🔐 Security

- Secrets are never committed — a single git‑ignored `.env` is the source of truth.
- JWT is stored in an **httpOnly cookie** (not in `localStorage`), mitigating token theft via XSS.
- BYOK API keys are encrypted at rest with **AES‑256‑GCM** (non‑extractable Web Crypto key).
- SSRF guards on server‑side URL fetching and DB connection targets; rate limiting on sensitive
  endpoints; prompt‑injection hardening on AI prompts.

## 📄 License

Released under the [MIT License](LICENSE). Update this if you prefer a different license.
