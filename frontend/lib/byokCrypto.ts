// BYOK API anahtarı için AES-256-GCM şifreleme (Web Crypto).
//
// Şifreleme anahtarı IndexedDB'de NON-EXTRACTABLE bir CryptoKey olarak saklanır:
// - Ham anahtar baytları hiçbir zaman JS'e / localStorage'a çıkmaz.
// - XSS çalışsa bile anahtarı DIŞARI ÇIKARAMAZ (exfiltrate edemez); yalnızca sayfa
//   canlıyken kullanabilir. localStorage'da yalnızca AES-GCM ciphertext (iv + tag) durur.
// Bu, önceki XOR/Base64 obfuscation'a göre gerçek, kimlik-doğrulamalı şifreleme sağlar.

const DB_NAME = 'namines-secure';
const STORE = 'keys';
const KEY_ID = 'byok-aes-key';
const IV_BYTES = 12;

function openDb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, 1);
    req.onupgradeneeded = () => req.result.createObjectStore(STORE);
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

function idbGet<T>(key: string): Promise<T | undefined> {
  return openDb().then(
    (db) =>
      new Promise<T | undefined>((resolve, reject) => {
        const tx = db.transaction(STORE, 'readonly');
        const req = tx.objectStore(STORE).get(key);
        req.onsuccess = () => resolve(req.result as T | undefined);
        req.onerror = () => reject(req.error);
      })
  );
}

function idbSet(key: string, value: unknown): Promise<void> {
  return openDb().then(
    (db) =>
      new Promise<void>((resolve, reject) => {
        const tx = db.transaction(STORE, 'readwrite');
        tx.objectStore(STORE).put(value, key);
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
      })
  );
}

async function getOrCreateKey(): Promise<CryptoKey> {
  const existing = await idbGet<CryptoKey>(KEY_ID);
  if (existing) return existing;
  // extractable = false → ham anahtar asla dışa aktarılamaz.
  const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, [
    'encrypt',
    'decrypt',
  ]);
  await idbSet(KEY_ID, key);
  return key;
}

const encoder = new TextEncoder();
const decoder = new TextDecoder();

function toBase64(bytes: Uint8Array): string {
  let bin = '';
  for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
  return btoa(bin);
}

function fromBase64(b64: string): Uint8Array {
  return Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
}

/** Düz metni AES-256-GCM ile şifreler; base64(iv | ciphertext) döner. */
export async function encryptSecret(plain: string): Promise<string> {
  if (!plain || typeof window === 'undefined' || !crypto?.subtle) return '';
  const key = await getOrCreateKey();
  const iv = crypto.getRandomValues(new Uint8Array(IV_BYTES));
  const cipher = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, encoder.encode(plain));
  const cipherBytes = new Uint8Array(cipher);
  const combined = new Uint8Array(iv.length + cipherBytes.length);
  combined.set(iv, 0);
  combined.set(cipherBytes, iv.length);
  return toBase64(combined);
}

/** base64(iv | ciphertext) çözer; başarısızsa boş string döner. */
export async function decryptSecret(b64: string): Promise<string> {
  if (!b64 || typeof window === 'undefined' || !crypto?.subtle) return '';
  try {
    const key = await getOrCreateKey();
    const data = fromBase64(b64);
    const iv = data.slice(0, IV_BYTES);
    const cipher = data.slice(IV_BYTES);
    const plain = await crypto.subtle.decrypt({ name: 'AES-GCM', iv }, key, cipher);
    return decoder.decode(plain);
  } catch {
    return '';
  }
}
