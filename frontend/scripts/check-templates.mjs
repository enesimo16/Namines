#!/usr/bin/env node
/**
 * Şablon bekçisi — her şablonu ürünün KENDİ kural motorundan geçirir.
 *
 * <b>Neden çalışan API'ye karşı, taklit bir kontrol değil:</b> "AI üretir, kural
 * motoru kanıtlar" diyen bir üründe, örnek şemaların o motordan geçememesi
 * iddiayı ilk temasta çürütür — ve şablonlar ürünün ilk temas yüzeyi
 * (second-phase/17). Kuralları burada yeniden yazmak, iki ayrı doğruluk kaynağı
 * üretmek olurdu; asıl soru "linter ne diyor", bunun tek cevabı linter'ın kendisi.
 *
 * Ayrıca her şablon **altı motorda da** derleniyor: bir tip yalnızca PostgreSQL'de
 * karşılığı olduğu için seçilmişse, bunu kullanıcı Oracle'ı seçince değil burada
 * öğrenmek gerekiyor.
 *
 * Çalıştırma (API ayakta olmalı):
 *   npm run check:templates
 *   NAMINES_API=http://localhost:5000 npm run check:templates
 */

import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const API = (process.env.NAMINES_API ?? 'http://localhost:5000').replace(/\/+$/, '');
const ENGINES = ['PostgreSQL', 'MySQL', 'MSSQL', 'SQLite', 'Oracle', 'MariaDB'];

/**
 * `lib/templates.ts` TypeScript; Node onu doğrudan içe aktaramıyor. Depoda zaten
 * devDependency olan tsc ile geçici bir dizine derleniyor — dosyanın tek
 * içe aktarımı yalnızca TİP olduğu için üretilen JS kendi kendine yetiyor.
 */
function loadTemplates() {
  const out = mkdtempSync(join(tmpdir(), 'namines-tpl-'));
  // tsc doğrudan çağrılıyor (npx üzerinden değil): npx, kabuk sarmalayıcısı
  // gerektiriyor ve Windows'ta spawnSync ile sessizce başarısız oluyordu.
  const tsc = join('node_modules', 'typescript', 'bin', 'tsc');
  const result = spawnSync(
    process.execPath,
    [tsc, 'lib/templates.ts', '--outDir', out, '--rootDir', '.', '--module', 'esnext',
     '--target', 'es2022', '--moduleResolution', 'bundler', '--skipLibCheck'],
    { encoding: 'utf8' },
  );

  if (result.status !== 0) {
    console.error(result.stdout || result.stderr || result.error?.message);
    throw new Error('templates.ts derlenemedi');
  }

  // --rootDir '.' sayesinde çıktı yolu belirli: kaynak ağacı olduğu gibi
  // korunuyor, yani templates.js her zaman lib/ altında.
  return {
    path: join(out, 'lib', 'templates.js'),
    cleanup: () => rmSync(out, { recursive: true, force: true }),
  };
}

async function post(path, body) {
  const response = await fetch(`${API}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  if (!response.ok) throw new Error(`${path} → HTTP ${response.status}`);
  return response.json();
}

const compiled = loadTemplates();
let TEMPLATES;
try {
  ({ TEMPLATES } = await import(pathToFileURL(compiled.path).href));
} finally {
  compiled.cleanup();
}

// Erişilebilirlik önce kontrol ediliyor: API kapalıyken 12 şablonun tamamının
// "başarısız" görünmesi, gerçek bir şablon hatasıyla karıştırılabilirdi.
try {
  await post('/api/lint', { name: 'ping', tables: [], relations: [] });
} catch (error) {
  console.error(`\n  API'ye ulaşılamadı (${API}).`);
  console.error('  Şablonlar gerçek kural motoruna karşı doğrulanıyor; API ayakta olmalı.\n');
  process.exit(2);
}

const failures = [];
let totalTables = 0;

for (const template of TEMPLATES) {
  const { key, label, schema } = template;
  const tables = schema.tables.length;
  const relations = schema.relations.length;
  totalTables += tables;

  const lint = await post('/api/lint', schema);
  const messages = lint.messages ?? [];
  const blocking = messages.filter(m => {
    const severity = String(m.severity).toLowerCase();
    return severity === 'error' || severity === 'warning' || severity === '2' || severity === '1';
  });

  // Bilgi seviyesindeki notlar (ör. "PascalCase olmalı") kasıtlı olarak
  // engellemiyor: snake_case, hedeflediğimiz motorlarda yerleşik gelenek ve
  // şablonları linter'ın bir tercihine uydurmak için değiştirmek, demoyu
  // güzelleştirmek uğruna gerçekçiliği bozmak olurdu.
  const notes = messages.length - blocking.length;

  const engineErrors = [];
  for (const engine of ENGINES) {
    try {
      const result = await post('/api/compile/sql', { schema, dbType: engine });
      if (!result.sql || result.sql.trim().length === 0) {
        engineErrors.push(`${engine}: boş DDL`);
        continue;
      }
      const blockingDiagnostics = (result.diagnostics ?? []).filter(d => d.severity === 'error');
      for (const diagnostic of blockingDiagnostics) {
        engineErrors.push(`${engine}: ${diagnostic.message}`);
      }
    } catch (error) {
      engineErrors.push(`${engine}: ${error.message}`);
    }
  }

  const ok = blocking.length === 0 && engineErrors.length === 0;
  const status = ok ? 'OK  ' : 'FAIL';
  console.log(
    `  ${status} ${label.padEnd(24)} ${String(tables).padStart(2)} tables · ` +
    `${String(relations).padStart(2)} relations · ${notes} notes`,
  );

  if (!ok) {
    failures.push({ key, blocking, engineErrors });
  }

  // 20 tablonun altındaki bir şablon, gerçek bir şemanın nasıl göründüğünü
  // göstermiyor — ve şablonların var olma sebebi tam olarak bu.
  if (tables < 20) {
    failures.push({ key, blocking: [], engineErrors: [`yalnızca ${tables} tablo — en az 20 bekleniyor`] });
  }
}

console.log(`\n  ${TEMPLATES.length} şablon · ${totalTables} tablo`);

if (failures.length > 0) {
  console.error('\n  Başarısız:');
  for (const failure of failures) {
    console.error(`\n  ${failure.key}`);
    for (const message of failure.blocking) console.error(`    · [${message.severity}] ${message.message}`);
    for (const message of failure.engineErrors) console.error(`    · ${message}`);
  }
  console.error('');
  process.exit(1);
}

console.log('  Hepsi ürünün kendi kural motorundan ve altı DDL üreticisinden geçti.\n');
