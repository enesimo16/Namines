#!/usr/bin/env node
/**
 * namines-diff.mjs
 * Compares two Namines schema JSON files and outputs a Markdown diff report.
 *
 * Usage:
 *   node scripts/namines-diff.mjs <base-schema.json> <head-schema.json>
 *
 * Exit codes:
 *   0  — no destructive changes
 *   1  — usage error
 *   2  — destructive changes detected (DROP TABLE / DROP COLUMN)
 *
 * The script is intentionally dependency-free (standard Node.js only).
 */

import { readFileSync } from 'fs';

// ── CLI args ──────────────────────────────────────────────────────────────────

const [,, baseFile, headFile] = process.argv;
if (!baseFile || !headFile) {
  console.error('Usage: node namines-diff.mjs <base.json> <head.json>');
  process.exit(1);
}

const readSchema = (path) => {
  // Strip UTF-8 BOM if present (Windows editors + PowerShell often add it)
  const text = readFileSync(path, 'utf8').replace(/^﻿/, '');
  const raw = JSON.parse(text);
  // Accept both camelCase (frontend export) and PascalCase (API response)
  return {
    name:      raw.name      ?? raw.Name      ?? 'Unnamed',
    tables:    raw.tables    ?? raw.Tables    ?? [],
    relations: raw.relations ?? raw.Relations ?? [],
  };
};

const base = readSchema(baseFile);
const head = readSchema(headFile);

// ── Index by id ───────────────────────────────────────────────────────────────

const toMap = (tables) => new Map(tables.map(t => [t.id ?? t.Id, t]));
const colMap = (cols)   => new Map(cols.map(c => [c.id ?? c.Id, c]));

const baseMap = toMap(base.tables);
const headMap = toMap(head.tables);

const allTableIds = new Set([...baseMap.keys(), ...headMap.keys()]);

// ── Gather changes ────────────────────────────────────────────────────────────

const addedTables    = [];
const droppedTables  = [];
const modifiedTables = [];   // { table, addedCols, droppedCols, modifiedCols }

for (const id of allTableIds) {
  const b = baseMap.get(id);
  const h = headMap.get(id);

  const bName = b?.name ?? b?.Name ?? '';
  const hName = h?.name ?? h?.Name ?? '';

  if (!b && h) { addedTables.push(hName);   continue; }
  if (b && !h) { droppedTables.push(bName); continue; }

  // Both exist — check columns
  const bCols = colMap(b.columns ?? b.Columns ?? []);
  const hCols = colMap(h.columns ?? h.Columns ?? []);
  const allColIds = new Set([...bCols.keys(), ...hCols.keys()]);

  const addedCols    = [];
  const droppedCols  = [];
  const modifiedCols = [];

  for (const cid of allColIds) {
    const bc = bCols.get(cid);
    const hc = hCols.get(cid);
    const bcName = bc?.name ?? bc?.Name ?? '';
    const hcName = hc?.name ?? hc?.Name ?? '';

    if (!bc && hc) { addedCols.push(hcName);   continue; }
    if (bc && !hc) { droppedCols.push(bcName);  continue; }

    // Check for meaningful changes (type, nullability, PK/FK)
    const changed =
      (bc.type ?? bc.Type)           !== (hc.type ?? hc.Type)           ||
      (bc.isNullable ?? bc.IsNullable) !== (hc.isNullable ?? hc.IsNullable) ||
      (bc.isPK ?? bc.IsPK)            !== (hc.isPK ?? hc.IsPK);

    if (changed) {
      modifiedCols.push({
        name:       hcName,
        baseType:   bc.type ?? bc.Type ?? '?',
        headType:   hc.type ?? hc.Type ?? '?',
        baseNull:   bc.isNullable ?? bc.IsNullable ?? false,
        headNull:   hc.isNullable ?? hc.IsNullable ?? false,
        basePk:     bc.isPK ?? bc.IsPK ?? false,
        headPk:     hc.isPK ?? hc.IsPK ?? false,
      });
    }
  }

  if (addedCols.length || droppedCols.length || modifiedCols.length || hName !== bName) {
    modifiedTables.push({ baseName: bName, headName: hName, addedCols, droppedCols, modifiedCols });
  }
}

// ── Render Markdown ───────────────────────────────────────────────────────────

const destructive = droppedTables.length > 0 || modifiedTables.some(t => t.droppedCols.length > 0);

const lines = [
  `## 🗃️ Namines Schema Diff`,
  ``,
  `> **Base:** \`${base.name}\` · **Head:** \`${head.name}\``,
  ``,
];

// Summary table
const summary = [
  addedTables.length    ? `✅ ${addedTables.length} table(s) added`   : null,
  droppedTables.length  ? `💥 ${droppedTables.length} table(s) dropped` : null,
  modifiedTables.length ? `🔧 ${modifiedTables.length} table(s) modified` : null,
].filter(Boolean);

if (summary.length === 0) {
  lines.push('_No schema changes detected._');
  console.log(lines.join('\n'));
  process.exit(0);
}

lines.push(summary.join(' · '), '');

if (destructive) {
  lines.push('> ⚠️ **Destructive changes detected.** This migration will DROP data. Review carefully before merging.', '');
}

// Added tables
if (addedTables.length) {
  lines.push('### ✅ Added tables', '');
  for (const t of addedTables) lines.push(`- \`${t}\``);
  lines.push('');
}

// Dropped tables
if (droppedTables.length) {
  lines.push('### 💥 Dropped tables _(destructive)_', '');
  for (const t of droppedTables) lines.push(`- ~~\`${t}\`~~`);
  lines.push('');
}

// Modified tables
for (const t of modifiedTables) {
  const title = t.headName !== t.baseName
    ? `\`${t.baseName}\` → \`${t.headName}\``
    : `\`${t.headName}\``;
  lines.push(`### 🔧 ${title}`, '');

  if (t.addedCols.length) {
    lines.push('**Added columns:**');
    for (const c of t.addedCols) lines.push(`- ➕ \`${c}\``);
  }
  if (t.droppedCols.length) {
    lines.push('**Dropped columns _(destructive)_:**');
    for (const c of t.droppedCols) lines.push(`- ❌ ~~\`${c}\`~~`);
  }
  if (t.modifiedCols.length) {
    lines.push('**Modified columns:**', '');
    lines.push('| Column | Before | After |');
    lines.push('|--------|--------|-------|');
    for (const c of t.modifiedCols) {
      const before = [c.baseType, c.baseNull ? 'NULL' : 'NOT NULL', c.basePk ? 'PK' : ''].filter(Boolean).join(' · ');
      const after  = [c.headType, c.headNull ? 'NULL' : 'NOT NULL', c.headPk  ? 'PK' : ''].filter(Boolean).join(' · ');
      lines.push(`| \`${c.name}\` | ${before} | ${after} |`);
    }
  }
  lines.push('');
}

// Footer
lines.push('---');
lines.push(`_Generated by [Namines](https://github.com/namines/namines) schema diff · ${new Date().toISOString().slice(0,10)}_`);

const output = lines.join('\n');
console.log(output);

process.exit(destructive ? 2 : 0);
