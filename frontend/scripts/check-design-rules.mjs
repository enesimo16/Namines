#!/usr/bin/env node
/**
 * Tasarım sistemi bekçisi — FRONTEND.md kurallarını CI'da zorlar.
 *
 * <b>Neden bir test, bir doküman değil:</b> FRONTEND.md §4 "ham hex sayısı 0"
 * diyordu ve denetim yapıldığında sayı 29'du. Yani kural yazılmıştı ama hiçbir
 * şey onu uygulamıyordu; kod sessizce kaydı ve doküman yanlış hale geldi.
 * Uygulanmayan bir kural, kuralın olmamasından kötüdür — çünkü ona güvenilir.
 *
 * Bu betik iki şeyi kontrol ediyor:
 *   1. `components/` ve `app/` altındaki .tsx dosyalarında ham hex renk
 *   2. Tailwind'e alternatif CSS-in-JS kütüphanesi sızmış mı
 *
 * Çalıştırma: `npm run check:design`
 */

import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, extname } from 'node:path';

const ROOTS = ['components', 'app'];
const HEX = /#[0-9a-fA-F]{3,8}\b/g;

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
  console.log('✓ Tasarım kuralları temiz: ham hex yok, Tailwind dışı stil kütüphanesi yok.');
  process.exit(0);
}

console.error(`\n✗ ${violations.length} tasarım kuralı ihlali:\n`);
for (const v of violations) {
  console.error(`  ${v.file}:${v.line}  [${v.kind}]`);
  console.error(`    ${v.detail}\n`);
}
console.error('FRONTEND.md §4 (token zorunluluğu) ve §5 (kütüphane zorunluluğu).\n');
process.exit(1);
