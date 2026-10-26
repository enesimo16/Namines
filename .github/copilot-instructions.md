# GitHub Copilot & Codex Instructions for Database Engineering

This workspace connects to the **Namines Enterprise MCP Server**, providing deterministic schema engineering, migration proofs, and multi-target code generation.

## How to use Namines MCP Tools:

1. **Schema Changes & Migrations:**
   - Do not guess whether a migration is safe or breaking.
   - Run `namines_analyze_impact` comparing the base and target schemas.
   - For `Breaking` or `Destructive` changes, pause and notify the developer with exact details before proceeding.
   - When verifying DDL, call `namines_prove_migration` to test it against an ephemeral database engine container.

2. **Code Generation:**
   - When asked to generate ORM schemas (Drizzle, Prisma, SQLAlchemy, Gorm) or types (TypeScript, Zod, Pydantic, C#), use `namines_generate_artifact` or `namines_generate_prisma`.
   - Never write ORM models by hand when Namines can deterministically generate them from verified schemas.

3. **Database Exploration & Desk:**
   - Inspect connected databases using `namines_desk_execute_query` and `namines_desk_browse_table`.
   - Introspect live databases using `namines_pull_schema`.

4. **Production Architecture Blueprints:**
   - Search certified production database templates with `namines_hub_search_blueprints` and `namines_hub_get_blueprint`.
