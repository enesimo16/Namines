# 34 — Senden Beklenenler

> Bu dosya, **kodun hazır olduğu ama senin bir hesap, bir sayı ya da bir karar
> vermen gerektiği için tamamlanamayan** işleri toplar. Hiçbiri diğer işleri
> bloke etmiyor — her biri geldiğinde ilgili yer birkaç saatte bağlanır.
>
> Kaynak: `CHECKLIST.md` → "Kodun beklediği kararlar/erişimler". Burası aynı
> listenin basit dille yazılmış ve tek başına okunabilir hâli.

**Genel kural:** Hiçbir API anahtarını, parolayı ya da token'ı sohbete
yapıştırma. Hepsi ortam değişkenine girer; sen sadece "aldım" de yeter.

---

## 1. Neon hesabı + `NEON_API_KEY`

**Ne yapman lazım:** [neon.tech](https://neon.tech) → kayıt ol (ücretsiz plan
yeterli) → bir proje aç → Settings'ten bir API key üret → `NEON_API_KEY` ortam
değişkenine koy.

**Neden:** Şu an her branch için sıfırdan bir PostgreSQL container'ı açıyoruz.
Bu çalışıyor ama **yerel geliştirme veritabanı** üretiyor ve saniyeler değil
onlarca saniye sürüyor. Neon'un copy-on-write branch'leri aynı işi anında yapar.

**Geldiğinde ne olur:** `IBranchDatabaseProvisioner`'ın **ikinci** bir
implementasyonu olarak takılır. Mevcut container yolu silinmez — Neon'a
erişimi olmayan kurulumlar çalışmaya devam eder.

**İlgili:** [06-DATA-PLANE.md](06-DATA-PLANE.md) §3

---

## 2. npm + GitHub hesabı (yayın)

**Ne yapman lazım:** npm hesabı aç ve `npm login` yap; GitHub'da repo'yu
yayınlamaya hazır hâle getir.

**Neden:** MCP sunucusu, npm sarmalayıcısı ve release workflow'u **hazır** ama
`npm publish` ve `git tag v0.1.0` atılmadı — ikisi de hesap istiyor.

**Geldiğinde ne olur:** Kullanıcılar `npx` ile tek komutta kurar. Şu an
kurulum için depoyu klonlamaları gerekiyor; bu, MCP'nin yayılmasının önündeki
tek engel.

**İlgili:** [33-MCP-AND-SKILL.md](33-MCP-AND-SKILL.md)

---

## 3. Gateway'in public alan adı

**Ne yapman lazım:** Bir adres seç — ör. `api.namines.com` — ve `namines.com`
sende değilse önce onu al.

**Neden:** Üretilen OpenAPI dosyasındaki `servers` bloğu ve üretilen
TypeScript SDK'nın taban URL'i buna bağlı. Şimdilik **göreli yol** kullanılıyor;
yani eject edilen bir SDK, Namines'in dışında çalıştırıldığında nereye
bağlanacağını bilmiyor.

**İlgili:** [08-GATEWAY-API.md](08-GATEWAY-API.md)

---

## 4. Plan başına rate limit sayıları

**Ne yapman lazım:** Üç sayı söyle — Free / Pro / Team planlarında **dakikada
kaç istek**.

**Neden:** [08-GATEWAY-API.md](08-GATEWAY-API.md) §5 yalnızca bir aralık
veriyor (600–10.000 rpm). Kodda limitleyici hazır, okunacak sayı yok.

**Not:** Bir sayı uydurup koymadım. Yanlış bir varsayılan, ya kullanıcıları
haksız yere kesip şikâyet üretir ya da ücretsiz planı bedava sınırsız yapar —
ikisi de sessizce olur.

---

## 5. Redis kararı (evet / hayır)

**Ne yapman lazım:** Sadece "kullanacağız" ya da "kullanmayacağız" de.

**Neden:** Şu an istek sayacı **tek sunucunun belleğinde**. İki API instance'ı
açarsan aynı kullanıcı iki katı hak kazanır — limit sessizce anlamını kaybeder.
Ayrıca [08 §6](08-GATEWAY-API.md)'daki metadata cache de buna bağlı.

**Not:** SignalR backplane'i için Redis zaten destekleniyor (G6), yani altyapı
tamamen yabancı değil.

---

## 6. Stripe fiyat/plan eşlemesi

**Ne yapman lazım:** Stripe'ta Team ve Enterprise için fiyat (price) kayıtları
oluştur, `price_...` kimliklerini ver.

**Neden:** Kod bugün `SubscriptionStatus`'tan yalnızca **Free/Pro** ayrımını
çıkarabiliyor. Team/Enterprise için ne bir plan alanı ne de Stripe tarafında
karşılığı var.

**Ayrıca:** Stripe Türkiye'de sınırlı — Paddle / LemonSqueezy araştırması hâlâ
açık bir madde (aşağıdaki "kod dışı işler"e bak).

**İlgili:** [22-BUSINESS-MODEL.md](22-BUSINESS-MODEL.md)

---

## 7. İki doküman sapmasının onayı

Aşağıdaki iki noktada dokümandan **bilerek** saptım. Kabul ediyorsan bir şey
yapmana gerek yok; etmiyorsan söyle, geri alırım.

### 7.1 `Authorization: Bearer` yerine `X-Namines-Key`

Aynı uçlarda JWT de kabul ediliyor. İkisini tek başlıkta taşımak, sunucuyu
"bu bir kullanıcı oturumu mu, yoksa bir API anahtarı mı?" diye tahmin etmeye
zorlardı. Yanlış tahmin, bir anahtarın oturum yetkileriyle çalışması demek
olabilirdi.

### 7.2 argon2id yerine SHA-256

argon2 **düşük entropili parolalar** için tasarlandı; yavaşlığı, insanların
seçtiği tahmin edilebilir parolalara karşı korur. Bizim API anahtarımız
256-bit rastgele — kaba kuvvetle denenmesi zaten imkânsız. argon2 burada
hiçbir güvenlik kazandırmaz, yalnızca **her isteğe gecikme ekler**.

Anahtarın kendisi hiçbir zaman saklanmıyor, yalnızca hash'i; karşılaştırma
sabit zamanlı.

**İlgili:** [13-SECURITY.md](13-SECURITY.md), [08-GATEWAY-API.md](08-GATEWAY-API.md) §4

---

## Kod dışı işler (bunlar da sende)

- [ ] `C:\Users\Enes Yel` dizinindeki yanlış git deposunu düzelt — remote'u
      `automated-recruitment-pipeline` görünüyor ve bu depoyla ilgisi yok.
- [ ] Ödeme altyapısı araştırması (Stripe TR sınırlı → Paddle / LemonSqueezy).
- [ ] `namines.com` alan adı + marka taraması.
- [ ] GitHub App oluşturma — Namines Bot'un PR'lara **yazabilmesi** için
      (bkz. [35-KALAN-BUYUK-ISLER.md](35-KALAN-BUYUK-ISLER.md) §5).
