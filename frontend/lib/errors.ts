/**
 * Hata gövdesi okuyucuları (FE-008 / B-57).
 *
 * **Neden bir yardımcı gerekti:** TypeScript, `catch` değişkenine YALNIZCA
 * `any` ya da `unknown` yazılmasına izin veriyor (`catch (e: MyType)` →
 * `TS1196`). Yani "tipli hata" diye bir seçenek yok; `any`'den kurtulmanın
 * tek yolu `unknown` yakalayıp daraltmak. Bu daraltmayı 26 çağrı noktasında
 * tekrarlamak yerine tek yerde topluyoruz.
 *
 * Sunucu hataları axios üzerinden geliyor ve gövde `err.response.data`
 * içinde; kod tabanı bu alanı bazen `message`, bazen `error` adıyla
 * okuyordu — ikisi de destekleniyor.
 */

/** Sunucunun hata gövdesi. İki ad da görülüyor: `message` ve `error`. */
export interface ApiErrorBody {
  message?: string;
  error?: string;
}

/** Axios benzeri hatanın gövdesi; yoksa `undefined`. */
export const apiErrorBody = (e: unknown): ApiErrorBody | undefined =>
  (e as { response?: { data?: ApiErrorBody } } | null | undefined)?.response?.data;

/**
 * HTTP durum kodu; yoksa `undefined`.
 *
 * Çağrı noktalarının çoğu bunu 429 (kota doldu) ve 403 (yetki) ayırmak için
 * kullanıyor — "bir hata oluştu" demekle "günlük AI limitin doldu" demek
 * arasındaki fark tam olarak burada.
 */
export const apiErrorStatus = (e: unknown): number | undefined =>
  (e as { response?: { status?: number } } | null | undefined)?.response?.status;

/**
 * Kullanıcıya gösterilecek mesaj.
 *
 * Sıra: sunucu gövdesi (`message` → `error`) → `Error.message` → yedek metin.
 *
 * **Boş dize YEDEĞE düşer, `??` DEĞİL `||` mantığı kullanılıyor:** çağrı
 * noktaları önceden `||` zinciriyle yazılmıştı ve sunucu boş bir `message`
 * döndürdüğünde yedek metni gösteriyordu. `??` kullanmak, kullanıcıya boş
 * bir hata kutusu göstermek anlamına gelirdi — davranış sessizce değişirdi.
 */
export const errorMessage = (e: unknown, fallback: string): string => {
  const body = apiErrorBody(e);
  const fromServer = body?.message || body?.error;
  if (fromServer) return fromServer;

  const native = e instanceof Error ? e.message : undefined;
  return native || fallback;
};
