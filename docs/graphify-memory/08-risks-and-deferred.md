# Namines — Risks & Explicitly Deferred Items

## Top-scored risks (doc 25-RISKS.md, scored by probability × impact)

- **R1 (score 25, critical): scope explosion crushes a solo developer.** This has already happened once, in Phase 1 — 21 controllers were built with zero tests and no index support, in just two months. Mitigation: freeze scope hard at the v2.0/Faz-3 boundary and sell the product before adding further scope.
- **R2 (score 15): the core thesis "Console creates retention" is untested.** The entire new-phase plan rests on this unvalidated assumption. A decision gate is set at week 34 ("Hafta-34"): if Console DAU-per-project stays below 2, or the team-invite rate stays below 20%, the plan pivots away from Console toward migration-safety/legacy-database-management (closer to Bytebase/Atlas) — fewer, larger contracts instead of a broad self-serve product.
- **R3 (score 10, but rated "existential" in impact): data loss or a cross-tenant data leak.** Mitigations: a mandatory pre-migration backup before any destructive operation, two-layer tenant isolation (application-level filter plus database-level Row-Level Security), 120 tenant-isolation tests that block CI, and a required 2-person approval for destructive schema changes.
- **R4 (score 16): an LLM vendor absorbs this feature directly into their own product.** Mitigation is to invest specifically in *operated infrastructure* an LLM vendor cannot itself provide — provisioning, RLS enforcement, team panels, an auditable approval trail — and to publish the MCP server as a distribution channel rather than treating coding agents as competitors.
- **R10 (score 15): founder burnout.** Mitigation: a sustained-pace guideline of ≤40 hours/week, a discipline of shipping at the end of every phase rather than batching risk, and building in public.
- **Additional yellow-tier risks:** AI-cost margin erosion (R5); Neon vendor lock-in (R6, mitigated by the `IDatabaseProvider` abstraction); codegen correctness risk reaching an actual customer (R7, mitigated by the golden-file test suite plus a nightly 275-database validation matrix); free-tier abuse (R8); known Phase-1 security debt (R9); Turkey-specific payment friction (R11, Stripe is limited in Turkey — Paddle/LemonSqueezy are under evaluation).

## "Kills the plan" scenarios and their fallbacks (bottom table of doc 25-RISKS.md)

- Console goes unused → pivot the whole product toward the migration-safety/legacy-database-management niche.
- Nobody pays by week 40 → aggressively cap the free tier and shift the business model toward consulting/tooling revenue.
- Technical scope isn't met by week 30 → drop the Data Plane entirely; keep BYODB-only (Console and Gateway still function, at zero infrastructure cost).
- AI cost becomes uncontrolled → push all AI-powered features entirely into the Pro tier; keep only deterministic (non-AI) features free.
- Solo-founder pace proves unsustainable → freeze scope at v1.8 (schema-design tool only, but the best one available) and sell it as a smaller, sustainable $9/mo product.

## Explicitly deferred, not rejected (doc 32-DEFERRED-NOT-REJECTED.md — full detail also in the product-identity doc)

Generic app builder, generic automation platform ("Namines Flow"), an AI Dataset Factory ("Namines Data"), unverified "%X performance improvement" marketing claims, and running Namines' own self-hosted PostgreSQL/Kubernetes cluster.

## Desk-specific deferred items (doc `namines_desk/09-YOL-HARITASI.md`)

- Applying DDL changes directly to a live database — blocked on Namines Vault existing.
- GitHub push deployment — blocked on a GitHub App account/credentials existing.
- Read-request logging — volume, retention, and cost model not yet designed.
- Per-project usage/billing — blocked on the `UsageEvent.ProjectId` schema gap.
- An API-key management screen — pushed to v1.1, since Desk v1 uses session auth rather than API keys.
- SSO handoff from the main app — pushed to v1.1, needs a new token-lifetime design.
- Live log streaming via SignalR — pushed to v1.1.

## Business-line ideas deliberately parked (doc 31-NEW-BUSINESS-LINES.md), in the doc's own priority order for after MVP

1. **MSSQL/Oracle → PostgreSQL migration-copilot** — flagged as "the most directly monetizable output of Faz 0," since the golden-file-test-plus-Testcontainers infrastructure built for Phase 0 effectively already *is* this product.
2. **Self-healing DBA** — needs real query telemetry first (same underlying gap as the "Database Doctor" rejection above).
3. **Agency white-label DBA channel.**
4. **KVKK/compliance audit report generator** — blocked on NSL's planned `@tag(pii)` annotation shipping.
5. **Tender/procurement documentation generator** — lowest priority, cheapest to add later, deliberately last.
