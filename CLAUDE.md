# Claude Code Playbook — Namines Enterprise MCP

Namines MCP Server is enabled in this repository. Use the `namines_*` tools whenever working with database schemas, migrations, or live queries.

## Essential Tool Guidelines

### 1. Verification Loop
1. **Analyze First:** Always run `namines_analyze_impact` before proposing a schema change or writing a migration.
2. **Follow Risk Tiers:**
   - `Safe`: Proceed.
   - `Risky`: Prove with `namines_prove_migration`.
   - `Breaking` / `Destructive`: Stop and obtain explicit developer sign-off.
3. **Prove on Real Engine:** Use `namines_prove_migration` to run the migration inside an isolated Docker container. Report exact engine error messages.

### 2. Multi-Target Code Generation
Use `namines_generate_artifact` to generate:
- `orm.drizzle` / `types.zod` / `types.typescript` (TypeScript stack)
- `orm.sqlalchemy` / `types.python` (Python stack)
- `contract.graphql` / `contract.protobuf` (API contracts)
- `orm.gorm` (Go stack)
- `types.csharp` (C# stack)
- `mig.flyway` / `mig.liquibase` (Enterprise migrations)
- `schema.prisma` (via `namines_generate_prisma`)

### 3. Live Database Queries
- Execute queries safely with `namines_desk_execute_query`.
- Explore table records with `namines_desk_browse_table`.
- Introspect live databases with `namines_pull_schema`.

### 4. Hub Architectural Blueprints
- Search verified schemas with `namines_hub_search_blueprints`.
- Retrieve full schemas with `namines_hub_get_blueprint`.
