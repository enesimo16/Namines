#!/usr/bin/env node
/**
 * Tasarım sistemi bekçisi — FRONTEND.md kurallarını CI'da zorlar.
 *
 * <b>Neden bir test, bir doküman değil:</b> FRONTEND.md §4 "ham hex sayısı 0"
 * diyordu ve denetim yapıldığında sayı 29'du. Yani kural yazılmıştı ama hiçbir
 * şey onu uygulamıyordu; kod sessizce kaydı ve doküman yanlış hale geldi.
 * Uygulanmayan bir kural, kuralın olmamasından kötüdür — çünkü ona güvenilir.
 *
 * Bu betik dört şeyi kontrol ediyor:
 *   1. `components/` ve `app/` altındaki .tsx dosyalarında ham hex renk
 *   2. Tailwind'e alternatif CSS-in-JS kütüphanesi sızmış mı
 *   3. Tailwind'in HAZIR renk aileleri (zinc/emerald/indigo/rose/orange...)
 *   4. Saf `text-white` / `bg-black` değerleri
 *
 * <b>3 ve 4 neden sonradan eklendi:</b> betik yalnızca `#hex` arıyordu ve
 * FRONTEND.md §8.3 "ham hex 0" diye kaydediyordu — doğruydu ama eksikti.
 * İhlaller hex yazmayı bırakıp `text-zinc-500` yazmaya geçmişti: ölçüm
 * yapıldığında **254 kullanım** vardı. Yani kural uygulanıyor sanılıyor, ihlal
 * ise sadece yazım biçimi değiştirmiş oluyordu.
 *
 * Bu önemliydi çünkü hazır aileler paletin dışında: `text-zinc-500` (#71717a)
 * koyu zeminde ~4.0:1, `text-zinc-600` (#52525b) ~2.6:1 — ikisi de WCAG AA
 * altı. §8.1'in "göz yoruyor"un ölçülebilir kısmı dediği şey tam olarak buydu
 * ve temizlenmiş sayılıyordu.
 *
 * Çalıştırma: `npm run check:design`
 */

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, extname } from 'node:path';

const ROOTS = ['components', 'app'];

/**
 * Renk merkezinin KENDİSİ — burada ham değer OLMAK ZORUNDA (ölçeğin tepesi
 * bir yerden başlamalı). Denetim bu dosyada yalnızca `:root` bloğunun DIŞINI
 * kontrol ediyor.
 *
 * <b>Neden sonradan eklendi:</b> betik yalnızca `.tsx` tarıyordu ve
 * `globals.css` hiç denetlenmiyordu. Ölçüm yapıldığında bu tek dosyada **248
 * ham renk** vardı — üstelik FRONTEND.md §2'nin açıkça yasakladığı indigo
 * (#4f46e5 / #6366f1), amber (#fbbf24), cyan (#06b6d4) ve Tailwind slate
 * grileri dahil. Header, sidebar, canvas araç çubuğu ve bağlam menüsü bu
 * dosyadan boyanıyor; yani ekranın büyük kısmı hiçbir zaman palete
 * bağlanmamıştı. "Rengi tek yerden değiştiremiyorum"un sebebi buydu.
 */
const TOKEN_CENTRE = 'app/globals.css';
const HEX = /#[0-9a-fA-F]{3,8}\b/g;

/**
 * Tailwind'in kendi renk ölçekleri — FRONTEND.md §2 paletin dışında hiçbir
 * aileye izin vermiyor. `--color-*` token'ları hepsinin karşılığını veriyor
 * (surface-* / content-* / accent-* / danger-* / success-*).
 */
const TAILWIND_PALETTE = [
  'slate', 'gray', 'zinc', 'neutral', 'stone',
  'red', 'orange', 'amber', 'yellow', 'lime', 'green', 'emerald', 'teal',
  'cyan', 'sky', 'blue', 'indigo', 'violet', 'purple', 'fuchsia', 'pink', 'rose',
];
const PALETTE_PREFIXES =
  'text|bg|border|from|to|via|ring|shadow|fill|stroke|divide|outline|decoration|accent|caret';
const PALETTE_CLASS = new RegExp(
  `(?<![\\w-])(?:${PALETTE_PREFIXES})-(?:${TAILWIND_PALETTE.join('|')})-\\d{2,3}(?![\\w-])`,
  'g',
);

/**
 * Saf beyaz/siyah. Alfa'lı biçimler (`bg-white/[0.06]`, `bg-scrim/70`) bilinçli
 * olarak DIŞARIDA: onlar bir renk değil, yüzeyin üstüne konan saydam bir kat —
 * kod tabanının yerleşik yüzey deseni. Yasaklanan, bir metnin ya da zeminin
 * SAF #fff/#000 olması.
 */
/**
 * JSX string prop'larindaki ham renk: `maskColor="rgba(0,0,0,.7)"` gibi.
 *
 * Sinif adi olmadiklari icin PALETTE_CLASS ve PURE_BW bunlari kaciriyordu; HEX
 * de yalnizca `#` biciminde olanlari goruyordu. Uc yerde React Flow minimap
 * maskesi ve tur overlay'i bu sekilde palet disinda kalmisti.
 */
const RAW_FUNCTIONAL_COLOR = /rgba?\(\s*\d/g;

const PURE_BW =
  /(?<![\w-])(?:text-white|bg-white|bg-black|text-black|border-white|border-black)(?![\w/-])/g;

/**
 * OKUNAMAYAN font boyutu — 10px'in altı.
 *
 * Ölçüldü: `text-[8px]`/`text-[9px]`/`text-[9.5px]` 75 kez kullanılıyordu.
 * 11px genel kabul gören okunabilirlik tabanı; `text-micro` token'ı bunu
 * karşılıyor (VERCEL_DESIGN_ADAPTATION.md §7).
 *
 * Not: 10px ve 11px keyfi kullanımları (414 adet) şimdilik geçiyor —
 * hepsini tek adımda taşımak çok geniş bir görsel değişiklik olurdu.
 * Bu kural en azından tabanın ALTINA inilmesini engelliyor.
 */
const UNREADABLE_FONT = /text-\[(?:[0-9]|[0-9]\.[0-9])px\]/g;

/**
 * TERK EDİLEN radius değerleri.
 *
 * Ölçüldü: sekiz radius değeri eşzamanlı kullanımdaydı ve aralarında hiçbir
 * ilişki yoktu. Görev dokümanının yasak listesinde birebir madde:
 * "multiple unrelated border-radius values". Vercel'de ölçülen: üç değer.
 *
 * Ölçek artık: --radius-control (6px) / --radius-card (10px) /
 * --radius-modal (14px) + rounded-full (yalnızca avatar ve nokta).
 *
 * `rounded-lg`/`rounded-xl` HENÜZ yasak değil — 415 kullanımları var ve
 * hepsini tek commit'te taşımak gözden geçirilemez bir diff üretirdi.
 * Yasaklananlar, ölçekte karşılığı OLMAYAN uç değerler.
 */
const DEPRECATED_RADIUS = /(?<![\w-])rounded-(?:sm|3xl)(?![\w-])/g;

/** Tailwind yerine geçmeye çalışan kütüphaneler — FRONTEND.md §5. */
const FORBIDDEN_IMPORTS = [
  'styled-components',
  '@emotion/styled',
  '@emotion/react',
  '@stitches/react',
  'framer-motion', // §5: "Framer Motion YOK, projede kurulu değil — ekleme"
];

function walk(dir, out = []) {
  for (const entry of readdirSync(dir)) {
    if (entry === 'node_modules' || entry === '.next') continue;
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) walk(full, out);
    else if (['.tsx', '.ts', '.css'].includes(extname(entry))) out.push(full);
  }
  return out;
}

/**
 * Bir satır gerçek bir ihlal mi, yoksa yorum/dokümantasyon mu?
 *
 * Yorumlarda hex geçmesi meşru: hangi rengin neden kaldırıldığını yazan
 * notlar var ve onları silmek gerekçeyi kaybettirir.
 */
function isComment(line) {
  const t = line.trim();
  return t.startsWith('//') || t.startsWith('*') || t.startsWith('/*');
}

const violations = [];

for (const root of ROOTS) {
  for (const file of walk(root)) {
    const lines = readFileSync(file, 'utf8').split('\n');
    const isTokenCentre = file.replace(/\\/g, '/').endsWith(TOKEN_CENTRE);

    // ÇOK SATIRLI yorum takibi. `isComment` yalnızca satır BAŞINA bakıyordu;
    // CSS blok yorumlarının ara satırları `*` ile başlamıyor ve "hangi renk
    // neden kaldırıldı" notları ihlal sayılıyordu.
    let inComment = false;
    // Renk merkezinde ham değer OLMAK ZORUNDA — ölçeğin tepesi bir yerden
    // başlamalı. Muafiyet yalnızca tanım bloklarına, gerisine değil.
    let inTokenBlock = false;

    lines.forEach((line, i) => {
      const trimmed = line.trim();

      const wasInComment = inComment;
      if (!inComment && trimmed.includes('/*') && !trimmed.includes('*/')) inComment = true;
      else if (inComment && trimmed.includes('*/')) inComment = false;
      if (wasInComment) return;

      if (isTokenCentre) {
        if (/^(:root\s*\{|@theme\b)/.test(trimmed)) inTokenBlock = true;
        else if (inTokenBlock && trimmed === '}') { inTokenBlock = false; return; }
        if (inTokenBlock) return;
      }

      if (!isComment(line)) {
        const found = line.match(HEX);
        if (found) {
          violations.push({
            file, line: i + 1, kind: 'ham hex',
            detail: `${found.join(', ')} — token kullan (bkz. app/globals.css) ya da lib/designTokens.ts'teki token()`,
          });
        }
      }

      if (!isComment(line)) {
        const palette = line.match(PALETTE_CLASS);
        if (palette) {
          violations.push({
            file, line: i + 1, kind: 'Tailwind hazır renk ailesi',
            detail: `${[...new Set(palette)].join(', ')} — FRONTEND.md §2: palet dışı aile. ` +
              'Nötrler surface-*/content-*, semantikler danger-*/success-*, vurgu accent-*.',
          });
        }

        const rawFn = line.match(RAW_FUNCTIONAL_COLOR);
        if (rawFn) {
          violations.push({
            file, line: i + 1, kind: 'ham rgb()/rgba()',
            detail: "FRONTEND.md §4: renk token'dan gelmeli. " +
              'Saydamlık için color-mix(in srgb, var(--color-…) N%, transparent).',
          });
        }

        const pure = line.match(PURE_BW);
        if (pure) {
          violations.push({
            file, line: i + 1, kind: 'saf beyaz/siyah',
            detail: `${[...new Set(pure)].join(', ')} — FRONTEND.md §2: saf #fff/#000 yok. ` +
              'Metin için content-*, karartma için bg-scrim/<alfa>.',
          });
        }

        const tiny = line.match(UNREADABLE_FONT);
        if (tiny) {
          violations.push({
            file, line: i + 1, kind: 'okunamayan font boyutu',
            detail: `${[...new Set(tiny)].join(', ')} — 10px altı okunabilirlik ` +
              'tabanının altında. En küçük adım: text-micro (11px).',
          });
        }

        const radius = line.match(DEPRECATED_RADIUS);
        if (radius) {
          violations.push({
            file, line: i + 1, kind: 'terk edilen radius',
            // NOT: buraya ÖRNEK BİR TAILWIND SINIFI YAZILMAZ — yorum içinde
            // bile. Tailwind v4 kaynak dosyaları düz metin olarak tarayıp
            // sınıf adı arıyor ve bu betik de taranan dosyalar arasında.
            // Boru işareti içeren bir arbitrary-value örneği yazıldığında
            // Tailwind onu gerçek bir sınıf sanıp geçersiz CSS üretti ve
            // derlemeyi komple kırdı ("Unexpected token Delim"). Yorum
            // yazmak kodu çalıştırmaz sanmak burada yanlış varsayımdı.
            detail: `${[...new Set(radius)].join(', ')} — ölçekte karşılığı yok. ` +
              'Ölçek: radius-control (6px) / radius-card (10px) / radius-modal (14px).',
          });
        }
      }

      for (const pkg of FORBIDDEN_IMPORTS) {
        if (line.includes(`from '${pkg}'`) || line.includes(`from "${pkg}"`)) {
          violations.push({
            file, line: i + 1, kind: 'yasak bağımlılık',
            detail: `${pkg} — FRONTEND.md §5: stil için yalnızca Tailwind CSS v4`,
          });
        }
      }
    });
  }
}

/**
 * TANIMSIZ TOKEN denetimi — `var(--color-x)` var ama `--color-x:` yok.
 *
 * <b>Neden gerekti:</b> bir turda "kullanılmıyor" sanılan 15 token silindi;
 * silme öncesi arama yalnızca `.tsx`/`.ts` dosyalarında yapıldı, renk
 * merkezinin KENDİSİNDE yapılmadı. 5 token / 9 kullanım tanımsız kaldı ve
 * `var()` sessizce miras alınan değere düştü — `.glass-panel` gölgesi ve
 * kod bloklarının rengi hiçbir hata vermeden bozuldu.
 *
 * Tanımsız bir CSS değişkeni ASLA hata vermiyor; bu yüzden yalnızca
 * otomatik bir kontrol yakalayabilir.
 */
const centreFile = ROOTS.map(r => walk(r))
  .flat()
  .find(f => f.replace(/\\/g, '/').endsWith(TOKEN_CENTRE));

if (centreFile) {
  const css = readFileSync(centreFile, 'utf8');
  const defined = new Set([...css.matchAll(/^\s*(--[\w-]+)\s*:/gm)].map(m => m[1]));
  const used = new Map();

  // Font değişkenleri DIŞARIDAN geliyor: `next/font` bunları `<html>`
  // üzerine runtime'da enjekte ediyor, CSS'te tanımlı olmaları beklenmiyor.
  const EXTERNAL = /^--font-/;

  for (const m of css.matchAll(/var\(\s*(--[\w-]+)/g)) {
    const name = m[1];
    if (!defined.has(name) && !EXTERNAL.test(name)) {
      used.set(name, (used.get(name) ?? 0) + 1);
    }
  }

  for (const [name, count] of used) {
    violations.push({
      file: centreFile, line: 0, kind: 'tanımsız token',
      detail: `${name} — ${count} yerde okunuyor ama hiç tanımlanmamış. ` +
        'Tanımsız var() sessizce miras alınan değere düşer, hata vermez.',
    });
  }
}

if (violations.length === 0) {
  console.log('✓ Tasarım kuralları temiz: ham hex yok, palet dışı Tailwind ailesi yok, ' +
    'saf beyaz/siyah yok, tanımsız token yok, Tailwind dışı stil kütüphanesi yok.');
  process.exit(0);
}

console.error(`\n✗ ${violations.length} tasarım kuralı ihlali:\n`);
for (const v of violations) {
  console.error(`  ${v.file}:${v.line}  [${v.kind}]`);
  console.error(`    ${v.detail}\n`);
}
console.error('FRONTEND.md §4 (token zorunluluğu) ve §5 (kütüphane zorunluluğu).\n');
process.exit(1);
