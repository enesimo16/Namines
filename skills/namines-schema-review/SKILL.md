---
name: namines-schema-review
description: Use when changing a database schema — adding, altering, renaming or dropping tables, columns, indexes or foreign keys, or writing a migration. Analyzes the change with a deterministic rule engine, proves the DDL against a real database engine, and enforces human approval for destructive changes. Works with PostgreSQL, MSSQL, MySQL, MariaDB, Oracle and SQLite.
---

# Namines schema review

## What this is for

You can already write a migration. What you cannot do on your own is **prove it is
safe before it runs**. That is what these tools add:

- `namines_analyze_impact` — a rule engine, not a model. Its findings are facts.
- `namines_prove_migration` — starts a real database container, runs the DDL, and
  reports what the engine actually said.
- `namines_generate_ddl` — deterministic DDL for 6 engines, golden-file tested.

Treat their output as evidence. Treat your own reasoning about risk as a hypothesis
that these tools confirm or refute.

## The loop

1. **Get the current state.** `namines_pull_schema` against the live database, or use
   the schema the user already has. Do not skip this and diff against an imagined
   baseline — the whole analysis is only as good as the base.
2. **Analyze before proposing.** Call `namines_analyze_impact` with the base and your
   proposed target. Do this *before* you show the user a migration, not after.
3. **Act on the risk level** (see below).
4. **Report what the tool found**, not your summary of what it probably means.

## Risk levels — what each one obliges you to do

| `overallRisk` | What you do |
|---|---|
| `Safe` | Proceed. Mention the change is Safe and why. |
| `Risky` | Call `namines_prove_migration` first. Only proceed if it passes. |
| `Breaking` | **Stop.** Show the breaking changes. Get explicit human approval. |
| `Destructive` | **Stop.** Show the data-loss risks by name. Get explicit human approval. |

"Stop" means stop. Do not apply the migration, do not run the DDL against the user's
database, and do not soften the finding to keep momentum. If the user asks you to
proceed anyway after seeing the findings, that is their call to make — proceed, and
state plainly what they accepted.

## Rules

**Never launder a prediction as a proof.** If `prove_migration` did not run, do not
say the migration is verified. `supported: false` means nothing was proven — it is
not a pass.

**Pass the engine's error through verbatim.** When a real engine rejects DDL, its raw
message is the most valuable thing you have (SQL Server's `Msg 1785` on multiple
cascade paths is the canonical example). Do not paraphrase, tidy, or summarize it.

**Feed tool output back verbatim.** `pull_schema` output goes into `analyze_impact`
unchanged. Hand-editing the JSON in between is how a schema silently becomes an empty
one and the analysis cheerfully reports "Safe, nothing changed".

**The engine is not a detail.** The same schema is valid on PostgreSQL and rejected by
SQL Server. Always pass the engine the user actually runs.

**Defaults never drift toward data loss.** If an engine cannot express a requested
referential action, the generator falls back to `NO ACTION`, never `CASCADE`. If you
find yourself proposing `CASCADE` to make an error go away, stop and say so instead.

**Ask before opening a change request.** `namines_open_change_request` creates
something other people will see. Confirm with the user first.

## Reporting findings

Lead with the risk level and the concrete findings — affected tables and columns by
name, and the engine's own words when it rejected something. Then say what you did or
what you need from the user. Keep your own commentary short; the tool's findings are
the substance.

If a data-loss risk names a column, name that column in your response. "There is some
risk of data loss" is not a useful sentence to a person deciding whether to approve.
