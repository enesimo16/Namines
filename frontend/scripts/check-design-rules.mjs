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
const PURE_BW =
  /(?<![\w-])(?:text-white|bg-white|bg-black|text-black|border-white|border-black)(?![\w/-])/g;

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
    else if (['.tsx', '.ts'].includes(extname(entry))) out.push(full);
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

    lines.forEach((line, i) => {
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

        const pure = line.match(PURE_BW);
        if (pure) {
          violations.push({
            file, line: i + 1, kind: 'saf beyaz/siyah',
            detail: `${[...new Set(pure)].join(', ')} — FRONTEND.md §2: saf #fff/#000 yok. ` +
              'Metin için content-*, karartma için bg-scrim/<alfa>.',
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

if (violations.length === 0) {
  console.log('✓ Tasarım kuralları temiz: ham hex yok, palet dışı Tailwind ailesi yok, ' +
    'saf beyaz/siyah yok, Tailwind dışı stil kütüphanesi yok.');
  process.exit(0);
}

console.error(`\n✗ ${violations.length} tasarım kuralı ihlali:\n`);
for (const v of violations) {
  console.error(`  ${v.file}:${v.line}  [${v.kind}]`);
  console.error(`    ${v.detail}\n`);
}
console.error('FRONTEND.md §4 (token zorunluluğu) ve §5 (kütüphane zorunluluğu).\n');
process.exit(1);
