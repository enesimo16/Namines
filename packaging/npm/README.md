# @namines/mcp

MCP server that gives an AI coding agent **deterministic** database schema analysis —
and the ability to **prove** a migration against a real engine before it runs.

```json
{
  "mcpServers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"]
    }
  }
}
```

No .NET required — the right binary is downloaded on install (or first run).

## Tools

| Tool | What it does | Needs Docker |
|---|---|---|
| `namines_pull_schema` | Read a live database's structure. Read-only. | no |
| `namines_analyze_impact` | Rule-engine impact report: breaking changes, data-loss risks, lock risks, rollback, overall risk level. | no |
| `namines_generate_ddl` | Deterministic DDL for 6 engines, golden-file tested. | no |
| `namines_prove_migration` | Runs the DDL against a real throwaway container and reports what the engine said. | yes (not for SQLite) |
| `namines_open_change_request` | Opens a Change Request for human approval on a Namines server. | no |

The first four run entirely on your machine. Your connection string never leaves it,
and no Namines account or server is needed. Only `namines_open_change_request` talks
to a server, and it needs `NAMINES_API_TOKEN`.

## Why not just let the agent write the migration?

It already can. What it cannot do is prove the result is safe. A language model
*predicts* risk; `namines_analyze_impact` *computes* it, and `namines_prove_migration`
starts a real database and finds out — catching things static analysis cannot, like
SQL Server's `Msg 1785` on multiple cascade paths.

## Configuration

| Variable | Purpose |
|---|---|
| `NAMINES_API_TOKEN` | Required only for `namines_open_change_request`. |
| `NAMINES_API_URL` | Namines server URL (default `http://localhost:5000`). |
| `Security__AllowPrivateDbHosts` | Set to `false` to block localhost/private-network databases. Defaults to allowed, since reading your own local database is the point. |

## Also available

```bash
dotnet tool install -g Namines.Mcp   # MCP server, if you have .NET
dotnet tool install -g Namines.Cli   # the same engine as a CLI: namines pull|diff|ddl|prove
```

MIT licensed.
