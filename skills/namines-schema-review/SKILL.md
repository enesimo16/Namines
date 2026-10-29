---
name: namines-schema-review
description: Comprehensive database lifecycle, migration verification, code generation, and live query tools. Analyzes schema changes with deterministic rule engines, proves migrations against real Docker engine containers, generates models across 19 targets (Prisma, Drizzle, SQLAlchemy, Zod, Pydantic, etc.), searches production Hub blueprints, and runs safe queries via Desk.
---

# Namines Database Lifecycle & Architecture Review

## Core Capabilities
- **`namines_analyze_impact`**: Deterministic schema risk analysis (`Safe`, `Risky`, `Breaking`, `Destructive`).
- **`namines_prove_migration`**: Runs DDL inside a throwaway container and reports true engine verdict.
- **`namines_generate_artifact`**: Generates source code for 19 targets:
  - ORMs: `orm.drizzle`, `orm.sqlalchemy`, `orm.gorm`, `orm.typeorm`, `orm.django`, `orm.sequelize`
  - Types: `types.typescript`, `types.zod`, `types.python` (Pydantic), `types.csharp`, `contract.graphql`, `contract.protobuf`
  - Migrations: `mig.flyway`, `mig.liquibase`
  - Canonical: `nsl`, `ir.json`
- **`namines_generate_prisma`**: Deterministic `schema.prisma` generator with engine warnings.
- **`namines_generate_ddl`**: 6-engine SQL DDL generation.
- **`namines_desk_execute_query`**: READ-ONLY SQL against a connected database: a single SELECT / WITH / EXPLAIN / SHOW statement. Writes, DDL, `SELECT ... INTO` and multi-statement batches are rejected; SQLite is not supported. It never modifies data.
- **`namines_desk_browse_table`**: High-density paginated table browsing.
- **`namines_hub_search_blueprints`** & **`namines_hub_get_blueprint`**: 10 curated production database architecture blueprints (starting points, not certifications) (FinTech, SaaS, ERP, EHR, AI Ops, IoT, etc.).
- **`namines_flow_match_rules`**: Evaluate event-driven schema automation policies.
- **`namines_open_change_request`**: Open human review request in Namines Cloud.

## The Verification Loop
1. **Get the current state:** `namines_pull_schema` against live DB or export from existing models.
2. **Analyze before proposing:** Call `namines_analyze_impact` with baseline and proposed schemas.
3. **Act on the risk level:**
   - `Safe`: Proceed.
   - `Risky`: Call `namines_prove_migration` first. Only proceed if it passes.
   - `Breaking`: **Stop.** Show the breaking changes. Get explicit human approval.
   - `Destructive`: **Stop.** Show data-loss risks by name. Get explicit human approval.
4. **Generate clean code:** Use `namines_generate_artifact` to sync ORM schemas, validation models, and types across all application layers.
