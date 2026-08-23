# 34 — Senden Beklenenler

> Bu dosya, **kodun hazır olduğu ama senin bir hesap, bir sayı ya da bir karar
> vermen gerektiği için tamamlanamayan** işleri toplar. Hiçbiri diğer işleri
> bloke etmiyor — her biri geldiğinde ilgili yer birkaç saatte bağlanır.
>
> Kaynak: `CHECKLIST.md` → "Kodun beklediği kararlar/erişimler". Burası aynı
> listenin basit dille yazılmış ve tek başına okunabilir hâli.

**Genel kural:** Hiçbir API anahtarını, parolayı ya da token'ı sohbete
yapıştırma. Hepsi ortam değişkenine girer; sen sadece "aldım" de yeter.

## Tek bakışta

| # | Ne | Tipi | Bunsuz ne olmuyor |
|---|----|------|-------------------|
| 1 | Neon hesabı + `NEON_API_KEY` | hesap | Branch veritabanı anında açılmıyor (şu an container, yavaş) |
| 2 | npm + GitHub yayını | hesap | MCP `npx` ile kurulamıyor; kullanıcı depoyu klonlamak zorunda |
| 3 | Gateway public alan adı | karar | Eject edilen SDK, Namines dışında nereye bağlanacağını bilmiyor |
| 4 | Plan başına rate limit sayıları | 3 sayı | Limitler kodda hazır, okunacak sayı yok |
| 5 | Redis: evet / hayır | karar | 2+ sunucuda rate limit anlamını kaybediyor |
| 6 | Stripe fiyat/plan eşlemesi | hesap | Team/Enterprise ayrımı yapılamıyor |
| 7 | İki doküman sapmasının onayı | onay | Bir şey bloke değil; itirazın varsa geri alırım |
| 8 | **GitHub App** (`AppId`, `PrivateKey`, `WebhookSecret`) | hesap | **Bot kod olarak hazır ama tek satır yazamıyor** |
| 9 | Groq API anahtarı | hesap | `/query/nl` bir kez bile gerçekten çalıştırılmadı |
| 10 | Disk alanı (şu an 4,8 GB) | makine | Control DB container'ı bu yüzden düştü |

En yüksek etkili ikisi: **8** (bot tamamen hazır, sadece kimlik bekliyor) ve
**2** (MCP'nin yayılmasının önündeki tek engel).

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

**Bilmen gereken bir düzeltme (G41):** Gateway'in tüm uçları, "pahalı uçlar"
için konmuş **dakikada 5 istek** limitini paylaşıyordu. Bu, Gateway'i normal bir
uygulama için kullanılamaz kılıyor ve anahtar başına ayarladığın limiti tamamen
ölü koda çeviriyordu — o sayıya ulaşmanın yolu yoktu. Gateway'e ayrı bir
politika verdim ve **son çare olarak dakikada 1200** seçtim. Bu bir tavan değil,
kimliği doğrulanmamış trafiğin sunucuyu meşgul etmesini engelleyen bir siper;
asıl sınır yukarıdaki üç sayıyla anahtar başına uygulanacak. 1200 sana yüksek
ya da düşük geliyorsa söyle.

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

## 8. GitHub App (Namines Bot)

**Ne yapman lazım:** GitHub → Settings → Developer settings → GitHub Apps →
New GitHub App.

- **İzinler:** Pull requests (read & write), Contents (read), Checks (write)
- **Abone olunacak olaylar:** `pull_request`, `issue_comment`
- **Webhook URL:** API'nin public adresi + `/api/github/webhook`
- **Webhook secret:** kendin bir değer üret

Sonra üç değeri ortam değişkenine koy: `Github__AppId`,
`Github__PrivateKey` (App'in indirdiğin `.pem` dosyasının içeriği),
`Github__WebhookSecret`.

**Neden:** Bot'un kodu **G43'te tamamlandı ve test edildi** — App kimlik
doğrulaması, PR yorumu, status check, `.nsl` okuma ve kırılma analizinin PR'a
bağlanması. Ama kimlik bilgisi yokken **yazmayı hiç denemiyor**: sahte bir
başarı raporlamak, çalıştığı sanılan ama hiçbir şey yapmayan bir özellik
bırakırdı. Yani bugün bot bir PR'a tek satır yazamıyor.

**Geldiğinde ne olur:** Hiçbir kod değişikliği gerekmiyor. Değerler tanımlandığı
anda bot her PR'da şema farkını inceleyip yorumu ve status check'i yazmaya
başlar; yıkıcı bir değişiklikte check `failure` döner ve merge korumaları
devreye girer.

**İlgili:** [11-MIGRATIONS-BRANCHING.md](11-MIGRATIONS-BRANCHING.md) §7

---

## 9. Groq API anahtarı (`/query/nl` için)

**Ne yapman lazım:** Groq anahtarını ortam değişkenine koy (proje zaten
`GroqAIService` kullanıyor, yeni bir şey kurman gerekmiyor).

**Neden:** Doğal dil sorgusu (`/query/nl`) **bir kez bile gerçekten
çalıştırılmadı.** Doğrulanan şeyler yalnızca kapılar: yetkisiz anahtar 403,
bilinmeyen motor reddi, kota ölçümü, hata mesajının sızdırmaması. Ama "üretilen
SQL doğru mu, model şemayı doğru okuyor mu" sorusunun cevabı **bilinmiyor**.

**Geldiğinde ne olur:** Gerçek bir soru sorulup üretilen SQL'in çalıştığı
doğrulanır. İstem düzeltmesi gerekirse orada görülür.

---

## 10. Disk alanı

**Ne yapman lazım:** C: sürücüsünde yer aç. Şu an **4,8 GB** boş.

**Neden:** Control DB container'ı bu yüzden 255 ile düştü (bu oturumda oldu).
Docker'ın kendi içinden 6,9 GB geri kazandım ama WSL2'nin sanal diski
kendiliğinden küçülmüyor — `wsl --shutdown` sonrası VHDX sıkıştırma gerekiyor.

**Not:** CLAUDE.md zaten uyarıyor — "Docker/build hataları illa kod hatası
değil, önce boş alanı kontrol et."

---

## Kod dışı işler (bunlar da sende)

- [ ] `C:\Users\Enes Yel` dizinindeki yanlış git deposunu düzelt — remote'u
      `automated-recruitment-pipeline` görünüyor ve bu depoyla ilgisi yok.
- [ ] Ödeme altyapısı araştırması (Stripe TR sınırlı → Paddle / LemonSqueezy).
- [ ] `namines.com` alan adı + marka taraması.
- [ ] API'nin public adresi (webhook'un ulaşabilmesi için) — §3 ve §8 ile aynı iş.
