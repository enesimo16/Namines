/**
 * Cookie ile kimlik doğrulanan yazma isteklerinde gönderilmesi zorunlu başlık.
 *
 * JWT `httpOnly` bir cookie'de taşınıyor — XSS'e karşı doğru tercih, ama
 * tarayıcı cookie'yi otomatik eklediği için CSRF'i mümkün kılıyor. Sunucu
 * (`CsrfProtectionMiddleware`) bu başlığı olmayan cookie'li yazma isteklerini
 * 403 ile reddediyor.
 *
 * **Koruma başlığın DEĞERİNDEN değil, gönderilebilmiş olmasından geliyor.**
 * Bir HTML formu ya da `<img>` etiketi özel başlık gönderemez; `fetch` ile
 * göndermek ise isteği preflight'a zorlar ve preflight sunucunun CORS
 * allowlist'ine takılır. Yani başlığın varlığı, isteğin izinli bir origin'den
 * geldiğinin tarayıcı tarafından doğrulanmış kanıtı.
 */
export const CSRF_HEADER = 'X-Namines-Request';

/** Yazma isteklerine eklenecek başlık nesnesi. */
export const csrfHeaders: Record<string, string> = { [CSRF_HEADER]: '1' };
