/**
 * Node ortamında eksik olan ve store'ların kullandığı asgari tarayıcı API'leri.
 *
 * **Neden taklit ediliyor, jsdom yerine:** yalnızca iki şey gerekiyor. Tam bir
 * DOM kurmak, testin ihtiyacı olmayan bir maliyet (her koşuya saniyeler).
 *
 * `requestAnimationFrame`, `useToastStore`'un çıkış animasyonunu tetiklemek
 * için kullanılıyor; testte ANINDA çalıştırılıyor (kuyruğa alınmıyor) — aksi
 * hâlde her test bir kare beklemek zorunda kalır ve zamanlamaya bağlı,
 * kırılgan testler doğardı.
 *
 * <b>`declare global` KULLANILMIYOR:</b> `@types/node` 22 bu iki adı zaten
 * tanımlıyor ve yeniden bildirmek `TS2300: Duplicate identifier` veriyor.
 * Atama yeterli; tip zaten var.
 */
const globals = globalThis as typeof globalThis & {
  requestAnimationFrame: (cb: (t: number) => void) => number;
  cancelAnimationFrame: (handle: number) => void;
};

globals.requestAnimationFrame = (cb: (t: number) => void) => {
  cb(0);
  return 0;
};

globals.cancelAnimationFrame = () => {};

export {};
