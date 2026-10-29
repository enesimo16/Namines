'use strict';

// Namines MCP binary indirici.
//
// Neden ayrı binary: sunucu .NET ile yazıldı (bkz. new-phase/33 §4), ama hedef
// kitle .NET geliştiricisi DEĞİL — Claude ile kendi DB'sinde çalışan geliştirici.
// `dotnet tool install` onlara runtime şartı koyup kurulumu ilk adımda öldürürdü.
// Bu yüzden self-contained binary indiriyoruz: kullanıcıda hiçbir ön koşul yok.

const fs = require('fs');
const path = require('path');
const os = require('os');
const https = require('https');
const { createHash } = require('crypto');

const REPO = process.env.NAMINES_MCP_REPO || 'enesimo16/Namines';
const VERSION = require('../package.json').version;
const BIN_DIR = path.join(__dirname, '..', 'bin');

// KRİTİK: stdout MCP protokol kanalıdır. Bu betik launcher'dan da çağrılabildiği
// için buradan stdout'a yazılan tek bir satır bile JSON-RPC akışını bozar.
// Her şey stderr'e gider.
function log(msg) {
  process.stderr.write(`[namines-mcp] ${msg}\n`);
}

function target() {
  const platform = os.platform();
  const arch = os.arch();

  const map = {
    'win32:x64': { rid: 'win-x64', exe: 'namines-mcp.exe' },
    'win32:arm64': { rid: 'win-arm64', exe: 'namines-mcp.exe' },
    'linux:x64': { rid: 'linux-x64', exe: 'namines-mcp' },
    'linux:arm64': { rid: 'linux-arm64', exe: 'namines-mcp' },
    'darwin:x64': { rid: 'osx-x64', exe: 'namines-mcp' },
    'darwin:arm64': { rid: 'osx-arm64', exe: 'namines-mcp' },
  };

  const hit = map[`${platform}:${arch}`];
  if (!hit) {
    throw new Error(
      `unsupported platform ${platform}/${arch}. ` +
        'Install the .NET tool instead: dotnet tool install -g Namines.Mcp'
    );
  }
  return hit;
}

function binaryPath() {
  return path.join(BIN_DIR, target().exe);
}

function get(url, redirectsLeft = 5) {
  return new Promise((resolve, reject) => {
    https
      .get(url, { headers: { 'User-Agent': 'namines-mcp-installer' } }, (res) => {
        if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
          if (redirectsLeft === 0) return reject(new Error('too many redirects'));
          res.resume();
          return resolve(get(res.headers.location, redirectsLeft - 1));
        }
        if (res.statusCode !== 200) {
          res.resume();
          return reject(new Error(`HTTP ${res.statusCode} for ${url}`));
        }
        const chunks = [];
        res.on('data', (c) => chunks.push(c));
        res.on('end', () => resolve(Buffer.concat(chunks)));
        res.on('error', reject);
      })
      .on('error', reject);
  });
}

async function download() {
  const { rid, exe } = target();
  const base = `https://github.com/${REPO}/releases/download/v${VERSION}`;
  const assetUrl = `${base}/namines-mcp-${rid}${exe.endsWith('.exe') ? '.exe' : ''}`;

  log(`downloading ${rid} binary for v${VERSION}...`);
  const bytes = await get(assetUrl);

  // FAIL-CLOSED: checksum doğrulanamıyorsa binary kurulmaz. Çalıştırılacak bir
  // yürütülebilir dosyada "doğrulanamadı ama yine de kuruldu" kabul edilemez;
  // ağ saldırganı hem binary'yi hem de checksums.txt'in yokluğunu taklit edebilir.
  let sums;
  try {
    sums = (await get(`${base}/checksums.txt`)).toString('utf8');
  } catch (err) {
    throw new Error(`could not fetch checksums.txt (${err.message}); refusing to install an unverified binary.`);
  }

  const assetName = path.basename(assetUrl);
  const expected = sums
    .split('\n')
    .map((l) => l.trim().split(/\s+/))
    // "hash  dosya" ya da "hash *dosya" (binary mod) biçimi; tam ad eşleşmesi
    // (endsWith, "xnamines-mcp-linux-x64" gibi bir ekle eşleşirdi).
    .find((p) => p.length >= 2 && p[1].replace(/^\*/, '') === assetName);

  if (!expected) {
    throw new Error(`no checksum published for ${assetName}; refusing to install an unverified binary.`);
  }

  const actual = createHash('sha256').update(bytes).digest('hex');
  if (actual !== expected[0].toLowerCase()) {
    throw new Error(`checksum mismatch for ${assetName}: expected ${expected[0]}, got ${actual}`);
  }
  log('checksum verified.');

  // Atomik yazım: yarım kalmış bir indirme `fs.existsSync(dest)` kontrolünü
  // geçip bozuk bir binary'yi sonsuza dek "kurulu" gösterirdi.
  fs.mkdirSync(BIN_DIR, { recursive: true });
  const dest = binaryPath();
  const tmp = `${dest}.${process.pid}.tmp`;
  try {
    fs.writeFileSync(tmp, bytes, { mode: 0o755 });
    fs.renameSync(tmp, dest);
  } catch (err) {
    try { fs.unlinkSync(tmp); } catch { /* yoksa sorun değil */ }
    throw err;
  }
  log(`installed: ${dest}`);
  return dest;
}

async function ensureBinary() {
  const dest = binaryPath();
  if (fs.existsSync(dest)) return dest;
  return download();
}

module.exports = { ensureBinary, binaryPath, target };

// postinstall olarak doğrudan çalıştırıldıysa indir. Hata durumunda kurulumu
// KIRMA (package.json'da `|| true`): ağ kısıtlı bir ortamda `npm i` tamamen
// başarısız olmak yerine, binary ilk çalıştırmada tembel olarak indirilir.
if (require.main === module) {
  ensureBinary().catch((err) => {
    log(`postinstall could not fetch the binary: ${err.message}`);
    log('it will be downloaded on first run instead.');
    process.exit(0);
  });
}
