#!/usr/bin/env node
'use strict';

// npx giriş noktası. `download.js` binary'yi garanti eder, sonra onu
// doğrudan bu process'in stdio'suna bağlayarak çalıştırır — MCP stdio
// protokolü bu yüzden: sunucu Claude ile stdin/stdout üzerinden konuşuyor,
// ara katman eklemek (ör. exec'in çıktısını buffer'lamak) protokolü bozar.

const { spawn } = require('child_process');
const { ensureBinary } = require('../lib/download');

function log(msg) {
  process.stderr.write(`[namines-mcp] ${msg}\n`);
}

async function main() {
  let binPath;
  try {
    binPath = await ensureBinary();
  } catch (err) {
    log(`could not obtain the namines-mcp binary: ${err.message}`);
    log('install the .NET tool instead: dotnet tool install -g Namines.Mcp');
    process.exit(1);
    return;
  }

  const child = spawn(binPath, process.argv.slice(2), { stdio: 'inherit' });

  child.on('error', (err) => {
    log(`failed to start ${binPath}: ${err.message}`);
    process.exit(1);
  });

  child.on('exit', (code, signal) => {
    if (signal) {
      process.kill(process.pid, signal);
      return;
    }
    process.exit(code === null ? 1 : code);
  });

  // MCP host bu process'i sonlandırırsa çocuğu da kapat — yoksa bağlantı
  // koptuktan sonra .NET tarafı arka planda takılı kalırdı.
  for (const sig of ['SIGINT', 'SIGTERM']) {
    process.on(sig, () => child.kill(sig));
  }
}

main();
