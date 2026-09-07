# Namines — Business Model & Go-To-Market

## Two competing pricing narratives

1. **Aspirational target model (new-phase doc 22-BUSINESS-MODEL.md, ~2026-08):** Free $0 / Pro $19/mo ($190/yr) / Team $39/user-mo ($390/user-yr) / Enterprise $1,500–8,000/mo. A caveat was inserted into this doc later: "this table describes the target model; the APPLIED price is different" — pointing readers to `PricingCatalog.cs` and second-phase doc 17.

2. **Actually shipped, evolving prices (second-phase, chronological):**
   - `16-KOTA-VE-MALIYET.md` snapshot: Pro $7.50/mo, Team $20/mo flat for a fixed 3 seats. Flagged in `03-PAZAR-VE-TASARIM-ANALIZI.md` as a genuine **pricing bug**: a 2-person team ends up paying more for less token allowance than 2× individual Pro subscriptions would cost. Recommendation: switch Team to per-seat pricing.
   - `17-ILK-TEMAS-VE-FIYAT.md` (later): the actual fix — **Pro $15/mo ($150/yr), Team $40/mo ($400/yr)**, annual discount 17% (~2 months free). Price is now served from a single source, `Namines.Core/Analysis/PricingCatalog.cs`, via `GET /api/subscription/plans`, rather than hard-coded inside a React component — this fixes a prior bug class where the UI could silently drift from what Stripe actually charges. At $15/mo Pro, net margin after the Stripe cut is roughly 89%.
   - **Stripe price IDs still not created as of the last check** (flagged 🔴 urgent in doc 34-SENDEN-BEKLENENLER.md §6) — the payment code is fully written and tested, but literally zero revenue is possible until 4 Stripe price identities exist in the Stripe dashboard.

## Quota model (evolved across docs 16-KOTA-VE-MALIYET.md → 36-KOTA-VE-AJAN.md)

- Free tier: 20,000 daily AI tokens, drawn from a shared pool that itself starts at 500,000 tokens/day (≈ $3.70/mo in underlying AI cost), growing over time via a `PoolPressureAsync` mechanism that recommends — but never auto-applies — pool increases.
- Pro: 200,000 tokens/day.
- Team: 1,000,000 tokens/day, shared across the team. Deliberately equal to Pro's total (not 3×), because "Team sells collaboration, not more tokens."
- Enterprise: 10,000,000 tokens/day.
- Gateway API rate limits: Free 60 rpm / Pro 600 rpm / Team 3,000 rpm / Enterprise 10,000 rpm. Gate G41 fixed a bug where the Gateway needed its own 1,200 rpm policy separate from a shared 5 rpm "sensitive-endpoint" limit that had been making the Gateway effectively unusable.
- Fair-share formula for the free pool: `pay = pool / targetFreeUsers`, capped at `min(planCap, max(pay × 2, usableFloor = 8000))`.

## Target segment (per the most candid self-assessment, doc 03-PAZAR-VE-TASARIM-ANALIZI.md)

The product is actually three layers:
- **(A) Schema design** — commodity; dbdiagram, ChartDB, Azimutt-level, and any general LLM can do this for free.
- **(B) Codegen** — commodity; the Prisma CLI does this for free.
- **(C) Change governance / proof** — genuinely defensible; the closest competitive analogs are Bytebase and Atlas.

**The defensible layer is (C), but the product markets itself as (A)** — a prompt-box homepage. This mismatch is flagged as **the single biggest strategic risk** identified in the self-assessment.

Recommended repositioning: *"Prove what will break before you ship a database change to production"* rather than *"design a database with AI."*

Recommended target customer: **small teams (2–5 people) running a production system without a dedicated DBA, and agencies/software houses** managing databases for multiple clients — explicitly not solo hobbyists (too much free alternative exists) and not yet enterprise (no SSO/SOC2 story exists yet).

## Differentiation vs. named competitors

- **Bytebase** — the most direct competitor in layer (C); more mature (open source, SOC2-certified, real enterprise references). Namines' claimed edges: it can *design* a schema from scratch (Bytebase only manages an existing one), it combines a deterministic gate with AI, and it has a clarifying-question agent.
- **Atlas (ariga.io)** — "Terraform for databases," the strongest rival on the "proof" claim specifically, but it is CLI-only and dev-first, with no visual screen a non-technical approver could use — this is Namines' claimed edge over it.
- **Supabase / Hasura / Directus** — budget rivals in the Backend-as-a-Service space, not "proof" rivals. Supabase does not support multiple database engines and does not offer hosting the way Namines is planning to eventually (a noted gap on Namines' own side, since Namines doesn't host user databases yet either).
- **Claude Code / Cursor** — both a rival and a complementary partner. The MCP strategy is explicitly framed as "become their tool rather than compete" — i.e. "Claude migration yazsın, Namines kanıtlasın" ("let Claude write the migration, let Namines prove it's safe").

## Turkey-specific GTM angle

None of the named competitors speak Turkish, address KVKK (Turkey's GDPR-equivalent regulation), focus on .NET/SQL Server, or keep customer data physically in Turkey. These four points are claimed repeatedly across docs 01-MARKET.md, 22-BUSINESS-MODEL.md, and 23-GTM.md as a genuine sales moat, especially with banks, insurance companies, holding-company IT departments, and public-sector vendors.

## GTM channels (doc 23-GTM.md)

- Shareable public schema pages (viral loop #1) — shipped in second-phase doc 17: added a share summary, a "designed with Namines" DBA badge, and a "Build your own" call-to-action linking to `/demo`.
- DBA README badge (viral loop #2).
- GitHub Bot PR comments (viral loop #3) — code complete but blocked on GitHub App credentials.
- Ejected-project attribution (viral loop #4).
- Blueprint Hub SEO pages (viral loop #5) — 20 real templates covering 384 tables shipped in second-phase doc 17, replacing 5 earlier toy templates of only 4–6 tables each.
- **MCP/Skill distribution** is explicitly called out (doc 03-PAZAR-VE-TASARIM-ANALIZI.md §1.7) as "the cheapest acquisition channel available" and recommended as a headline GTM investment rather than a side feature.
