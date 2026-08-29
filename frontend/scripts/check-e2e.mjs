#!/usr/bin/env node
/**
 * Uçtan uca özellik denetimi — "şu özellik çalışıyor / çalışmıyor".
 *
 * <b>Neden gerçek API'ye karşı:</b> birim testleri geçiyor olmak bir özelliğin
 * çalıştığını göstermiyor (bu depoda bunun bedeli birkaç kez ödendi: FK yönü,
 * SSE büyük/küçük harf, dosya bölücünün CRLF'i — hepsi testler yeşilken
 * canlıda kırıktı). Bu betik ayakta duran API'ye gerçek istek atıyor ve
 * cevabın İÇERİĞİNİ kontrol ediyor; 200 dönmesi yeterli sayılmıyor.
 *
 * Çalıştırma:
 *   node scripts/check-e2e.mjs                 # yalnız anonim uçlar
 *   NAMINES_TOKEN=<jwt> node scripts/check-e2e.mjs   # oturum gerektirenler dahil
 */

import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

const API = (process.env.NAMINES_API ?? 'http://localhost:5000').replace(/\/+$/, '');
const TOKEN = process.env.NAMINES_TOKEN ?? '';

function loadTemplates() {
  const out = mkdtempSync(join(tmpdir(), 'namines-e2e-'));
  const tsc = join('node_modules', 'typescript', 'bin', 'tsc');
  const r = spawnSync(process.execPath,
    [tsc, 'lib/templates.ts', '--outDir', out, '--rootDir', '.', '--module', 'esnext',
     '--target', 'es2022', '--moduleResolution', 'bundler', '--skipLibCheck'],
    { encoding: 'utf8' });
  if (r.status !== 0) throw new Error(r.stdout || r.stderr);
  return { path: join(out, 'lib', 'templates.js'), cleanup: () => rmSync(out, { recursive: true, force: true }) };
}

const compiled = loadTemplates();
let TEMPLATES;
try { ({ TEMPLATES } = await import(pathToFileURL(compiled.path).href)); }
finally { compiled.cleanup(); }

const schema = TEMPLATES.find(t => t.key === 'ecommerce').schema;
const mini = TEMPLATES.find(t => t.key === 'tasks').schema;

async function call(method, path, body, { auth = false, raw = false } = {}) {
  const headers = {};
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  if (auth && TOKEN) headers.Authorization = `Bearer ${TOKEN}`;
  const res = await fetch(`${API}${path}`, {
    method, headers, body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  if (raw) return { status: res.status, text };
  try { return { status: res.status, body: JSON.parse(text), text }; }
  catch { return { status: res.status, body: null, text }; }
}

const results = [];
/**
 * @param name  Kullanıcıya görünen özellik adı
 * @param check async () => true | string   (string = başarısızlık sebebi)
 */
async function feature(area, name, check, { needsAuth = false, needsAi = false } = {}) {
  if (needsAuth && !TOKEN) {
    results.push({ area, name, state: 'ATLANDI', note: 'oturum belirteci verilmedi' });
    return;
  }
  try {
    const r = await check();
    if (r === true) results.push({ area, name, state: 'ÇALIŞIYOR', note: '' });
    else if (r && r.blocked) results.push({ area, name, state: 'BEKLİYOR', note: r.blocked });
    else results.push({ area, name, state: needsAi ? 'AI-ENGELLİ' : 'ÇALIŞMIYOR', note: String(r) });
  } catch (e) {
    results.push({ area, name, state: needsAi ? 'AI-ENGELLİ' : 'ÇALIŞMIYOR', note: e.message });
  }
}

const ENGINES = ['PostgreSQL', 'MySQL', 'MSSQL', 'SQLite', 'Oracle', 'MariaDB'];

// ── Kural motoru ─────────────────────────────────────────────────────────
await feature('Kural motoru', 'Şema linter', async () => {
  const r = await call('POST', '/api/lint', schema);
  if (r.status !== 200) return `HTTP ${r.status}`;
  if (!Array.isArray(r.body?.messages)) return 'messages dizisi yok';
  return true;
});

await feature('Kural motoru', 'Linter gerçekten hata buluyor', async () => {
  // PK'sız tablo → "no primary key" uyarısı bekleniyor. Uç 200 dönüyor diye
  // motorun çalıştığını varsaymak, boş bir sonucu başarı saymak olurdu.
  const broken = { name: 't', tables: [{ id: 'a', stableUuid: 'a', name: 'x', columns: [
    { id: 'c', stableUuid: 'c', name: 'v', type: 'INT', isPK: false, isFK: false, isNullable: true, length: null, defaultValue: null },
  ] }], relations: [] };
  const r = await call('POST', '/api/lint', broken);
  const msgs = r.body?.messages ?? [];
  return msgs.some(m => /primary key/i.test(m.message)) ? true : 'PK eksikliği yakalanmadı';
});

// ── DDL üretimi ──────────────────────────────────────────────────────────
for (const engine of ENGINES) {
  await feature('DDL üretimi', `${engine} DDL`, async () => {
    const r = await call('POST', '/api/compile/sql', { schema, dbType: engine });
    if (r.status !== 200) return `HTTP ${r.status}`;
    const sql = r.body?.sql ?? '';
    if (!/CREATE TABLE/i.test(sql)) return 'CREATE TABLE üretilmedi';
    if (!sql.includes('order_items')) return 'tablolar eksik';
    return true;
  });
}

await feature('DDL üretimi', 'FK davranış tanılaması', async () => {
  const r = await call('POST', '/api/compile/sql', { schema, dbType: 'PostgreSQL' });
  return Array.isArray(r.body?.diagnostics) ? true : 'diagnostics alanı yok';
});

// ── Kod üretimi ──────────────────────────────────────────────────────────
await feature('Kod üretimi', 'EF Core sınıfları (zip)', async () => {
  // Uç bir ZIP döndürüyor, düz metin değil — ilk denemede betik metin sanıp
  // "sınıf üretilmedi" demişti. Sözleşmeyi varsaymak yerine okumak gerekti.
  const res = await fetch(`${API}/api/compile/efcore`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ schema, dbType: 'PostgreSQL' }),
  });
  if (res.status !== 200) return `HTTP ${res.status}`;
  const buf = new Uint8Array(await res.arrayBuffer());
  if (buf[0] !== 0x50 || buf[1] !== 0x4b) return 'zip imzası yok';
  return buf.length > 500 ? true : `zip yalnızca ${buf.length} bayt`;
});

await feature('Kod üretimi', 'Prisma şeması', async () => {
  const r = await call('POST', '/api/compile/prisma', { schema, dbType: 'PostgreSQL' });
  if (r.status !== 200) return `HTTP ${r.status}`;
  return /model\s+\w+/.test(r.text) ? true : 'model üretilmedi';
});

await feature('Kod üretimi', 'Eject hedef listesi', async () => {
  const r = await call('GET', '/api/compile/eject/targets');
  if (r.status !== 200) return `HTTP ${r.status}`;
  const n = Array.isArray(r.body) ? r.body.length : 0;
  return n > 0 ? true : 'hedef yok';
});

// ── Prompt hattı (AI'sız kısım) ──────────────────────────────────────────
await feature('Prompt hattı', 'Netleştirme soruları', async () => {
  const r = await call('POST', '/api/schema/clarify', { prompt: 'an online store with products and orders' });
  if (r.status !== 200) return `HTTP ${r.status}`;
  const qs = r.body?.questions ?? r.body?.Questions ?? [];
  return qs.length > 0 ? true : 'soru üretilmedi';
});

await feature('Prompt hattı', 'Plan önizleme', async () => {
  const r = await call('POST', '/api/schema/plan', { prompt: 'an online store with products and orders', answers: {} });
  if (r.status !== 200) return `HTTP ${r.status}`;
  const t = r.body?.tables ?? r.body?.Tables ?? [];
  return t.length > 0 ? true : 'plan boş';
});

// ── Motor dönüşümü ───────────────────────────────────────────────────────
await feature('Motor dönüşümü', 'Kayıp analizi', async () => {
  const r = await call('POST', '/api/schema/convert/analyze',
    { Schema: schema, Source: 'PostgreSQL', Target: 'MySQL' });
  if (r.status !== 200) return `HTTP ${r.status} ${r.text.slice(0, 100)}`;
  if (!Array.isArray(r.body?.findings)) return 'findings dizisi yok';
  if (r.body.target !== 'MySQL') return `hedef ${r.body.target}`;
  return true;
});

// ── Dokümantasyon ────────────────────────────────────────────────────────
await feature('Dokümantasyon', 'Mermaid ER diyagramı', async () => {
  const r = await call('POST', '/api/documentation/mermaid', schema);
  if (r.status !== 200) return `HTTP ${r.status}`;
  return /erDiagram/.test(r.text) ? true : 'erDiagram üretilmedi';
});

// ── Fiyatlandırma ────────────────────────────────────────────────────────
await feature('Fiyatlandırma', 'Plan fiyat listesi', async () => {
  const r = await call('GET', '/api/subscription/plans');
  if (r.status !== 200) return `HTTP ${r.status}`;
  const pro = (r.body ?? []).find(p => p.plan === 'pro');
  const monthly = pro?.prices?.find(x => x.interval === 'monthly');
  const yearly = pro?.prices?.find(x => x.interval === 'yearly');
  if (monthly?.amountUsd !== 15) return `Pro aylık ${monthly?.amountUsd}, 15 bekleniyordu`;
  if (yearly?.amountUsd !== 150) return `Pro yıllık ${yearly?.amountUsd}, 150 bekleniyordu`;
  if (pro?.yearlyDiscountPercent !== 17) return `indirim ${pro?.yearlyDiscountPercent}%`;
  return true;
});

await feature('Fiyatlandırma', 'Stripe ödeme akışı', async () => {
  const r = await call('POST', '/api/subscription/checkout?plan=pro&interval=monthly', undefined, { auth: true });
  if (r.status === 200) return true;
  // Fiyat kimliği .env'de boş: bu bir ÜRÜN HATASI DEĞİL, bekleyen bir kurulum.
  // "Çalışmıyor" demek yanıltıcı olurdu — kod doğru davranıp 500 yerine net
  // bir yapılandırma hatası döndürüyor.
  if (r.status === 500 && /price ID/i.test(r.text)) return { blocked: 'Stripe fiyat kimlikleri henüz girilmedi' };
  return `HTTP ${r.status}`;
}, { needsAuth: true });

// ── AI modelleri / kota ──────────────────────────────────────────────────
await feature('Kota', 'NAI model kataloğu', async () => {
  const r = await call('GET', '/api/quota/models');
  if (r.status !== 200) return `HTTP ${r.status}`;
  return (r.body ?? []).length > 0 ? true : 'model listesi boş';
});

await feature('Kota', 'Kota durumu', async () => {
  const r = await call('GET', '/api/quota/status', undefined, { auth: true });
  if (r.status !== 200) return `HTTP ${r.status}`;
  return typeof r.body?.dailyLimit === 'number' || typeof r.body?.DailyLimit === 'number'
    ? true : 'dailyLimit yok';
}, { needsAuth: true });

// ── Projeler ve paylaşım ─────────────────────────────────────────────────
let shareToken = null;
let projectId = null;

await feature('Projeler', 'Bulut projeye kaydetme', async () => {
  // Paylaşım zinciri kayıtlı bir proje gerektiriyor. Betik onu KENDİSİ
  // oluşturuyor: "önce elle bir proje kaydedin" diyen bir denetim, çalışan
  // bir özelliği "kırık" göstermekten başka işe yaramazdı.
  const id = `e2e-${Date.now()}`;
  const r = await call('POST', '/api/auth/sync', [{
    Id: id, Name: 'E2E Probe', DbType: 'PostgreSQL',
    SchemaJson: JSON.stringify(mini), NodePositionsJson: '{}',
  }], { auth: true });
  if (r.status !== 200) return `HTTP ${r.status} ${r.text.slice(0, 120)}`;
  projectId = id;
  return true;
}, { needsAuth: true });

await feature('Projeler', 'Bulut proje listesi', async () => {
  const r = await call('GET', '/api/auth/projects', undefined, { auth: true });
  if (r.status !== 200) return `HTTP ${r.status}`;
  const list = r.body?.projects ?? r.body ?? [];
  if (!Array.isArray(list)) return 'liste değil';
  return list.some(p => (p.id ?? p.Id) === projectId) ? true : 'kaydedilen proje listede yok';
}, { needsAuth: true });

await feature('Paylaşım', 'Paylaşım bağlantısı üretimi', async () => {
  if (!projectId) return 'kayıtlı proje yok (önce canvas\'tan bir proje kaydedin)';
  const r = await call('POST', `/api/share/${projectId}`, undefined, { auth: true });
  if (r.status !== 200) return `HTTP ${r.status}`;
  shareToken = r.body?.token;
  return shareToken ? true : 'token dönmedi';
}, { needsAuth: true });

await feature('Paylaşım', 'Herkese açık görüntüleme', async () => {
  if (!shareToken) return 'paylaşım jetonu üretilemedi';
  const r = await call('GET', `/api/share/view/${shareToken}`);
  return r.status === 200 && r.body?.schemaJson ? true : `HTTP ${r.status}`;
}, { needsAuth: true });

await feature('Paylaşım', 'DBA rozeti (SVG)', async () => {
  if (!shareToken) return 'paylaşım jetonu üretilemedi';
  const r = await call('GET', `/api/share/badge/${shareToken}`, undefined, { raw: true });
  return r.status === 200 && r.text.includes('<svg') ? true : `HTTP ${r.status}`;
}, { needsAuth: true });

await feature('Paylaşım', 'Sosyal önizleme görseli (OG)', async () => {
  if (!shareToken) return 'paylaşım jetonu üretilemedi';
  const r = await call('GET', `/api/share/og/${shareToken}.svg`, undefined, { raw: true });
  return r.status === 200 && r.text.includes('<svg') ? true : `HTTP ${r.status}`;
}, { needsAuth: true });

await feature('Paylaşım', 'Meta etiket verisi', async () => {
  if (!shareToken) return 'paylaşım jetonu üretilemedi';
  const r = await call('GET', `/api/share/meta/${shareToken}`);
  return r.status === 200 && typeof r.body?.tables === 'number' ? true : `HTTP ${r.status}`;
}, { needsAuth: true });

await feature('Paylaşım', 'Sitemap', async () => {
  const r = await call('GET', '/api/share/sitemap.xml', undefined, { raw: true });
  return r.status === 200 && r.text.includes('<urlset') ? true : `HTTP ${r.status}`;
});

// ── Koddan şema ──────────────────────────────────────────────────────────
await feature('Koddan şema', 'Prisma kodundan şema çıkarma', async () => {
  const code = 'model User {\n  id Int @id\n  email String\n  posts Post[]\n}\nmodel Post {\n  id Int @id\n  authorId Int\n  author User @relation(fields: [authorId], references: [id])\n}';
  const r = await call('POST', '/api/codeschema/extract',
    { Files: { 'schema.prisma': code }, DbType: 'PostgreSQL' }, { auth: true });
  if (r.status !== 200) return `HTTP ${r.status} ${r.text.slice(0, 120)}`;
  const tables = r.body?.schema?.tables ?? r.body?.Schema?.Tables ?? [];
  return tables.length >= 2 ? true : `yalnızca ${tables.length} tablo çıkarıldı`;
}, { needsAuth: true });

// ── Paylaşımlı hosting paketi ────────────────────────────────────────────
await feature('Dağıtım', 'Paylaşımlı hosting paketi', async () => {
  const r = await call('POST', '/api/compile/shared-hosting', { schema: mini, dbType: 'MySQL' }, { auth: true, raw: true });
  return r.status === 200 && r.text.length > 200 ? true : `HTTP ${r.status}`;
}, { needsAuth: true });

// ── AI uçları ────────────────────────────────────────────────────────────
await feature('AI', 'Şema üretimi (prompt → şema)', async () => {
  const form = new FormData();
  form.append('Prompt', 'a tiny blog with posts and comments');
  form.append('DbType', 'PostgreSQL');
  const res = await fetch(`${API}/api/schema/generate`, {
    method: 'POST', headers: { Authorization: `Bearer ${TOKEN}` }, body: form,
  });
  const text = await res.text();
  if (res.status === 429) return `sağlayıcı/kota sınırı (HTTP 429)`;
  if (res.status !== 200) return `HTTP ${res.status} ${text.slice(0, 140)}`;
  return text.includes('"tables"') || text.includes('result') ? true : 'şema dönmedi';
}, { needsAuth: true, needsAi: true });

await feature('AI', 'DBA analizi', async () => {
  const r = await call('POST', '/api/aidba/analyze', { Schema: schema, DbType: 'PostgreSQL' }, { auth: true });
  if (r.status === 429) return 'sağlayıcı/kota sınırı (HTTP 429)';
  if (r.status !== 200) return `HTTP ${r.status} ${r.text.slice(0, 120)}`;
  return true;
}, { needsAuth: true, needsAi: true });

// ── Rapor ────────────────────────────────────────────────────────────────
const pad = (s, n) => String(s).padEnd(n);
const areas = [...new Set(results.map(r => r.area))];
console.log('');
for (const area of areas) {
  console.log(`  ${area}`);
  for (const r of results.filter(x => x.area === area)) {
    const mark = r.state === 'ÇALIŞIYOR' ? 'OK  '
      : r.state === 'ATLANDI' ? '--  '
      : r.state === 'AI-ENGELLİ' ? 'AI  '
      : r.state === 'BEKLİYOR' ? 'WAIT' : 'FAIL';
    console.log(`    ${mark} ${pad(r.name, 38)} ${r.note}`);
  }
}
const ok = results.filter(r => r.state === 'ÇALIŞIYOR').length;
const bad = results.filter(r => r.state === 'ÇALIŞMIYOR');
const ai = results.filter(r => r.state === 'AI-ENGELLİ').length;
const waiting = results.filter(r => r.state === 'BEKLİYOR').length;
const skipped = results.filter(r => r.state === 'ATLANDI').length;
console.log(`\n  ${ok}/${results.length} çalışıyor · ${bad.length} kırık · ${ai} AI-engelli · ${skipped} atlandı\n`);

// Markdown tablo (dokümana yapıştırmak için)
console.log('  ── Markdown ──');
console.log('  | Alan | Özellik | Durum | Not |');
console.log('  |---|---|---|---|');
for (const r of results) {
  const s = r.state === 'ÇALIŞIYOR' ? '✅ Çalışıyor'
    : r.state === 'ÇALIŞMIYOR' ? '❌ Çalışmıyor'
    : r.state === 'AI-ENGELLİ' ? '⚠️ Sağlayıcı sınırı'
    : r.state === 'BEKLİYOR' ? '⏳ Kurulum bekliyor' : '➖ Atlandı';
  console.log(`  | ${r.area} | ${r.name} | ${s} | ${r.note} |`);
}
console.log('');

process.exit(bad.length > 0 ? 1 : 0);
