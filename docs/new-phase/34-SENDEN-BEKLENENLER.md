# 34 — Senden Beklenenler

> Bu dosya, **kodun hazır olduğu ama senin bir hesap, bir sayı ya da bir karar
> vermen gerektiği için tamamlanamayan** işleri toplar. Hiçbiri diğer işleri
> bloke etmiyor — her biri geldiğinde ilgili yer birkaç saatte bağlanır.
>
> Kaynak: `CHECKLIST.md` → "Kodun beklediği kararlar/erişimler". Burası aynı
> listenin basit dille yazılmış ve tek başına okunabilir hâli.
>
> **Son güncelleme:** G54 (GitHub entegrasyonu — depo taraması, çoklu geliştirici
> birleştirme, kaynak şeridi). 1983 backend + 117 frontend test yeşil.

**Genel kural:** Hiçbir API anahtarını, parolayı ya da token'ı sohbete
yapıştırma. Hepsi ortam değişkenine (`.env`) girer; sen sadece "aldım" de yeter.
`.env` `.gitignore`'da, depoya hiç girmiyor.

## Önce sade hâli (teknik bilgi gerekmez)

Aşağıdakilerin **hiçbiri kod eksikliği değil.** Hepsinin kodu yazıldı ve test
edildi; eksik olan tek şey senin bir hesap açman, bir sayı söylemen ya da bir
karar vermen.

### 🔴 Bu ikisi ürünü şu an tutuyor

**B) Stripe hesabı ve DÖRT fiyat**
Ödeme kodu tamamen hazır: Pro 15$/ay (150$/yıl) ve Team 40$/ay (400$/yıl),
checkout, webhook, plan ayrımı, iptal, portal, aylık/yıllık geçişi — hepsi
yazıldı ve çalışırken doğrulandı. **Ama tek kuruş tahsil edemez**, çünkü
Stripe'ta bu fiyatların karşılığı yok.
→ Stripe'ta dört fiyat oluştur, dört `price_...` kimliğini `.env`'e koy. §6

### 🟡 Sırada bekleyen dördü

**C) GitHub App** — Namines Bot hazır: PR'da "bu değişiklik şu tabloyu siliyor"
diye yorum yazacak ve riskliyse merge'ü engelleyecek. Şu an tek satır yazamıyor,
çünkü GitHub'a "ben Namines'im" diyebileceği kimliği yok. §8

**D) npm hesabı** — MCP sunucusu paketlendi, yayına hazır. Şu an kullanıcının
onu kurması için tüm projeyi indirmesi gerekiyor. §2

**E) Alan adı** — `namines.com` sende mi? Bir de API için adres lazım
(`api.namines.com` gibi). Kullanıcının indirdiği kod, Namines'in dışında
nereye bağlanacağını bilmiyor. Bu aynı zamanda C'nin de ön koşulu — GitHub'ın
webhook'u gönderebileceği bir adres gerekiyor. §3

**F) Kalıcı Groq anahtarı — ARTIK 🔴 ACİL, sebebi değişti** — Anahtarın süresi
değil, **katmanı** sorun. Ücretsiz katman dakikada yalnızca ~6.000 token
veriyor ve bu oturumda 5'ten fazla kez üretim bu duvara çarptı. Kotamız
doluyken bile `429` alıyoruz; yani **ürünü kendi makinemizde bile uçtan uca
deneyemiyoruz.**

→ Yapılacak: Groq konsolunda **kart tanımla** (Developer katmanı). Ön ödeme
yok, abonelik yok, sabit ücret yok — sadece kullandığın token. Karşılığında
limit **10 katına** çıkıyor (60.000 token/dk) ve token fiyatların **%25**
düşüyor. Küçük kullanımda tipik gider $5-20/ay.

→ Bu, DeepSeek/başka sağlayıcıya geçmekten hem **daha ucuz** hem **kod
değişikliği sıfır**. Karşılaştırma: second-phase/16-KOTA-VE-MALIYET.md. §9

### ✅ Sen "kafana göre karar ver" dedin — hepsi kapatıldı

**K) Ücretsiz havuz büyüklüğü — 500.000 token/gün** — onaylandı, değişmedi.

**L) Çoklu DB ilişki sınırı — Free 3 / Pro 25 / Team 100** — onaylandı,
değişmedi.

**G) Üç rate limit sayısı — Free 60 / Pro 600 / Team 3.000 / Enterprise
10.000** — onaylandı, değişmedi. §4

**H) Redis — hayır (şimdilik)** — tek sunucudayız, ekstra işletim yükü
almaya değmez; çoklu sunucuya geçince tekrar gündeme gelir. §5

**I) İki küçük teknik sapma (`X-Namines-Key`, SHA-256)** — kabul edildi. §7

**J) Neon hesabı** — 🟢 tek gerçek "isteğe bağlı" madde kaldı. Branch
veritabanları container ile açılıyor, çalışıyor ama yavaş; canın istediğinde
[neon.tech](https://neon.tech)'te ücretsiz bir hesap açıp `NEON_API_KEY`
verirsen anında açılmaya geçer. Vermezsen mevcut yol çalışmaya devam eder. §1

---

## ✅ Kapananlar (artık bir şey yapman gerekmiyor)

| Ne | Nasıl kapandı |
|----|---------------|
| **Groq anahtarı yokluğu** | Anahtar `.env`'de; şema üretimi ve netleştirme ajanı gerçek bir modele karşı uçtan uca doğrulandı. ⚠️ Ama anahtar 1 günlük — bkz. F |
| **`/query/nl` hiç denenmemişti** | Groq bağlandı, gerçek üretim yapıldı |
| **Plan başına rate limit sayısı yoktu** | Varsayılanlar kondu (60/600/3.000/10.000); artık kararın değil onayın bekleniyor |
| **Team/Enterprise ayrımı yapılamıyordu** | `PlanCode` alanı eklendi; webhook Stripe fiyatından planı okuyor. Kalan tek şey fiyat kimlikleri |
| **Fiyatlar belirsizdi** | Karar verildi: Free 0$, Pro 15$/ay veya 150$/yıl, Team 40$/ay veya 400$/yıl (3 koltuk) |
| **Fiyat ekranda düz metindi** | Tek kaynak `PricingCatalog`; ekran `GET /api/subscription/plans` ile okuyor (second-phase/17) |
| **Ürünü görmek için hesap gerekiyordu** | `/demo` — girişsiz, AI'sız, ama gerçek linter + gerçek DDL (second-phase/17) |
| **Geliştirici hesabı sürekli unutuluyordu** | `.env`'den açılışta kendiliğinden kurulan, sınırsız, görünmeyen Dev hesabı |
| **Kota gerçek harcamayı ölçmüyordu** | Sağlayıcının `usage` bloğu okunuyor; sabit tahmin yerine gerçek token düşülüyor (second-phase/16) |
| **Eşzamanlı isteklerle kota delinebiliyordu** | Bütçe önden rezerve ediliyor, iş bitince gerçekle mutabakat yapılıyor |
| **Ücretsiz havuzda ilk 5 kullanıcı her şeyi tüketiyordu** | Adil pay + kullanışlı taban; havuz hedef kullanıcı sayısına bölünüyor |
| **Model maliyet çarpanı üretimde ölü koddu** | Kota artık `ölçülen token × model çarpanı` |
| **Çoklu DB ilişkilerinde plan sınırı yoktu** | Free 3 / Pro 25 / Team 100 (bkz. L) |
| **Yıllık plan yoktu** | Aylık/yıllık geçişi eklendi; %17 indirim fiyatlardan hesaplanıyor |

---

## Tek bakışta

| # | Ne | Tipi | Aciliyet | Bunsuz ne olmuyor |
|---|----|------|----------|-------------------|
| 6 | **Stripe hesabı + 4 fiyat kimliği** (aylık + yıllık) | hesap | 🔴 | **Ödeme kodu hazır ama tek kuruş tahsil edemiyor** |
| 9 | **Groq'a kart tanımla** (Developer katmanı) | hesap | 🔴 | Ücretsiz katman dakikada ~6.000 token — ürünü kendi makinemizde bile uçtan uca deneyemiyoruz |
| 8 | GitHub App (`AppId`, `PrivateKey`, `WebhookSecret`) | hesap | 🟡 | Bot hazır ama PR'a tek satır yazamıyor; canlı domain gerekmez, tünelle test edilir |
| 2 | npm yayını | hesap | 🟡 | CI'a bağlandı (G54) — tek eksik `NPM_TOKEN` repo secret'ı |
| 3 | Public alan adı (`api.namines.com`?) | karar | 🟡 | Eject edilen SDK nereye bağlanacağını bilmiyor; webhook adresi de buna bağlı |
| 1 | Neon hesabı | hesap | 🟢 | Branch DB'ler yavaş açılıyor (ama açılıyor) |
| 4 | Rate limit sayıları | ✅ | — | Varsayılan **onaylandı** (60/600/3.000/10.000), değişiklik istersen söyle |
| 5 | Redis | ✅ | — | **Hayır** — tek sunucudayız, gerek yok; çoklu sunucuya geçince tekrar konuşuruz |
| 7 | İki doküman sapması | ✅ | — | **Kabul edildi** (`X-Namines-Key`, SHA-256) |
| K | Ücretsiz havuz 500K/gün | ✅ | — | **Onaylandı** |
| L | Çoklu DB sınırı 3/25/100 | ✅ | — | **Onaylandı** |
| 10 | ~~Disk alanı~~ | — | — | Kullanıcı bunun sorun olmadığını söyledi, madde kapatıldı |
| 11 | Desk v2 SSO devir jetonunun taşınma şekli | karar | ✅ | Karar verildi VE kodlandı — POST form (b), aşağıda ne yapıldığı yazıyor |
| 12 | Desk v2 toplu silme onay eşiği | karar | ✅ | 10 olarak onaylandı VE kodlandı |
| 13 | Desk v2'de oturum yolunda ham SQL'e izin — güvenlik kararı | karar | ✅ | İzin verildi (Owner + açık onay + çok katmanlı güvenlik) VE kodlandı |
| 14 | Desk v2 uyarı kuralları için e-posta altyapısı | hesap | 🟡 | Bilinçli olarak ŞİMDİLİK ertelendi — e-posta altyapısı gelene kadar E5.3 uygulanmayacak |

**En yüksek etkili ikisi:** **6** (Stripe — ürünün para kazanmasının önündeki
tek engel) ve **9** (Groq kartı — bu oturumda üretimi 5+ kez test edemedim,
hep TPM duvarına çarptı).

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

## 2. npm yayını — G54'te otomasyona bağlandı, tek eksik bir token

**Bu oturumda yapıldı:**
- Eksik olan `packaging/npm/bin/namines-mcp.js` yazıldı — `package.json`
  `bin/namines-mcp.js`'i gösteriyordu ama dosya hiç yoktu, yani paket
  yayınlansa bile çalışmazdı. Şimdi bu dosya `download.js`'in indirdiği
  platforma özgü binary'yi bulup çalıştırıyor.
- `.github/workflows/release.yml`'e `publish-npm` işi eklendi: `v*` etiketi
  atıldığında GitHub Release'i bekliyor, `package.json`'daki sürümü etiketle
  eşitliyor, sonra `npm publish` çalıştırıyor.

**Senin yapman gereken TEK şey:**
1. [npmjs.com](https://www.npmjs.com) → hesap aç (yoksa).
2. Hesap → **Access Tokens** → **Generate New Token** → **Automation** türünde
   bir token oluştur, kopyala.
3. GitHub'da bu depo → **Settings → Secrets and variables → Actions** →
   **New repository secret** → adı `NPM_TOKEN`, değeri kopyaladığın token.

Bu üçü bittiğinde bana söyle — `git tag v0.1.0 && git push origin v0.1.0`
komutunu ben atarım, geri kalanı (build, GitHub Release, npm publish) otomatik
yürür. Token yoksa iş kırmızıya düşmüyor, sadece "NPM_TOKEN yok, atlanıyor"
diyip geçiyor — yani token'ı sonra eklesen de sorun olmaz.

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

## 4. Plan başına rate limit sayıları — ✅ onaylandı

**Durum değişti:** Eskiden kodda okunacak sayı yoktu. Artık
[`PlanQuotas`](../../backend/Namines.Core/Analysis/PlanQuotas.cs)'ta duruyorlar:

| Plan | Gateway istek/dakika | Günlük AI token |
|------|---------------------|-----------------|
| Free | 60 | 20.000 |
| Pro | 600 | 200.000 |
| Team | 3.000 | 200.000 (Pro ile aynı — bilerek) |
| Enterprise | 10.000 | 10.000.000 |

**Ne yapman lazım:** Sadece bak ve "olur" ya da "şu şöyle olsun" de. Değiştirmek
tek satır ve tek yerde — sayılar tek bir dosyada duruyor.

**Team'in AI bütçesi neden Pro ile aynı:** Team'in sattığı şey daha çok token
değil, birlikte çalışma (3 koltuk, ortak workspace, paylaşılan projeler).
Token'ı da katlamak, ekip başına maliyeti üç katına çıkarıp 20$ fiyatı anlamsız
kılardı. İtiraz edersen değiştiririm.

**Bilmen gereken bir düzeltme (G41):** Gateway'in tüm uçları, "pahalı uçlar"
için konmuş **dakikada 5 istek** limitini paylaşıyordu. Bu, Gateway'i normal bir
uygulama için kullanılamaz kılıyordu. Gateway'e ayrı bir politika verdim ve son
çare olarak **dakikada 1200** seçtim — bu bir tavan değil, kimliği doğrulanmamış
trafiğin sunucuyu meşgul etmesini engelleyen bir siper.

---

## 5. Redis kararı — ✅ hayır (şimdilik)

**Karar:** Tek sunucudayız, gerçek bir sorun yok — Redis eklemek bu aşamada
gereksiz bir işletim yükü olurdu. Çoklu sunucuya (yatay ölçekleme) geçilince
bu madde otomatik olarak tekrar açılır, o zaman zorunlu hâle gelir.

**Neden bekliyorduk:** Şu an istek sayacı **tek sunucunun belleğinde**. İki API instance'ı
açarsan aynı kullanıcı iki katı hak kazanır — limit sessizce anlamını kaybeder.
Ayrıca [08 §6](08-GATEWAY-API.md)'daki metadata cache de buna bağlı.

**Not:** SignalR backplane'i için Redis zaten destekleniyor (G6), yani altyapı
tamamen yabancı değil.

---

## 6. Stripe hesabı + DÖRT fiyat kimliği — ✅ test modunda tamamlandı (G54)

`.env`'deki dört `price_...` Stripe API'sine karşı tek tek doğrulandı: Pro
$15/ay + $150/yıl, Team $40/ay + $400/yıl, hepsi `active: true`. Webhook
endpoint'i de kuruldu (`api/webhooks/stripe`, `checkout.session.completed` +
`customer.subscription.updated` + `customer.subscription.deleted`), secret
`.env`'e yazıldı. **Kalan tek şey:** test kartıyla (`4242 4242 4242 4242`) bir
checkout'u uçtan uca deneyip webhook'un gerçekten planı güncellediğini
görmek — istersen frontend'i açıp bunu birlikte yapalım.

**Canlıya geçince** (ABD şirketin hazır olduğunda) aynı 6 adımı **live mode**
Stripe panelinde tekrarlaman gerekiyor — test moduyla canlı modun ürünleri/
fiyatları/anahtarları birbirinden tamamen ayrı, `sk_test_` → `sk_live_` olur.

---

<details>
<summary>Orijinal kurulum talimatı (referans için saklandı)</summary>

### 🔴 ACİL (artık tamamlandı, yukarıya bak)

> **Güncellendi:** fiyatlar değişti ve yıllık planlar eklendi, yani artık iki
> değil **dört** fiyat gerekiyor. Aylık ve yıllık, Stripe'ta **ayrı ayrı fiyat**
> olarak kurulmalı — aynı kimliği iki döneme vermek, "yıllık" düğmesinin aylık
> abonelik açması demek ve bu ekrandan anlaşılmaz.

**Ne yapman lazım:**
1. Stripe hesabı aç (yoksa).
2. Dört **fiyat (price)** oluştur:

   | Plan | Dönem | Tutar |
   |---|---|---|
   | **Pro** | aylık | **15,00 $** |
   | **Pro** | yıllık | **150,00 $** |
   | **Team** | aylık | **40,00 $** |
   | **Team** | yıllık | **400,00 $** |

   Yıllıklar %17 indirimli (2 ay bedava). Bu oran ekranda **hesaplanıyor**,
   yazılı değil — tutarları değiştirirsen rozet kendiliğinden doğrulanır.

3. Değerleri `.env`'e koy:
   ```
   Stripe__SecretKey=sk_...
   Stripe__ProPriceId=price_...          # Pro aylık
   Stripe__ProYearlyPriceId=price_...    # Pro yıllık
   Stripe__TeamPriceId=price_...         # Team aylık
   Stripe__TeamYearlyPriceId=price_...   # Team yıllık
   ```
   Ayrıca webhook için `Stripe__WebhookSecret=whsec_...`

**Eksik kimlik ürünü bozmuyor:** kurulmamış bir dönemin düğmesi ekranda pasif
görünüyor ve sebebi yazıyor ("Checkout is not set up yet") — 500 veren bir
düğmeye tıklatmaktan iyi. Yani önce yalnızca aylıkları kurup yıllığı sonra
eklemek de mümkün.

**Neden acil:** Ödeme tarafının **kodu tamamen bitti** — checkout iki planı da
biliyor (`?plan=pro|team`), webhook hangi fiyatın ödendiğini okuyup kullanıcının
planını yazıyor (`PlanCode`), iptal Free'ye düşürüyor, portal çalışıyor,
kullanıcı ayarlarda üç plan kartını görüyor. **Tek eksik, Stripe tarafında bu
fiyatların var olmaması.** Yani ürün bugün satış yapamıyor ve bunun sebebi kod
değil.

**Tutar kodda, KİMLİK yapılandırmada** (`Namines.Core/Analysis/PricingCatalog.cs`).
Kimlik ortama göre değişiyor (test/canlı) ve bir sırdan çok bir adres; koda
gömmek test anahtarıyla canlıya çıkmak demekti. Tutarın kodda olmasının sebebi
ise ekranın onu okuyor olması: fiyat daha önce bir React bileşeninin içinde düz
metindi ve Stripe'takiyle ayrışsa kullanıcı farkı ancak kart ekstresinde görürdü.
**Stripe'ta tutarı değiştirirsen `PricingCatalog`'u da güncelle.**

**Ayrıca:** Stripe Türkiye'de sınırlı — Paddle / LemonSqueezy araştırması hâlâ
açık bir madde (aşağıdaki "kod dışı işler"e bak). Kod tarafı Stripe'a yazıldı;
başka bir sağlayıcıya geçersen webhook ve checkout değişir, plan modeli aynı kalır.

**İlgili:** [22-BUSINESS-MODEL.md](22-BUSINESS-MODEL.md)

</details>

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

> **Bunu App olmadan da kullanabilirsin — App yalnızca ÜÇ şeyi kilitliyor:**
> private depo taraması, bot'un PR'a yazması ve anonim kotanın 60'tan
> 5000/saate çıkması. **Public bir depodan şema okumak zaten çalışıyor**
> (`+` menüsü → GitHub repository — G53'te eklendi, App gerektirmiyor).

**Ne yapman lazım:** GitHub → Settings → Developer settings → GitHub Apps →
New GitHub App.

- **İzinler:** Pull requests (read & write), Contents (read), Checks (write)
- **Abone olunacak olaylar:** `pull_request`, `issue_comment`
- **Webhook URL:** API'nin public adresi + `/api/github/webhook` — **canlıya
  çıkmadan da olur**, `cloudflared tunnel --url http://localhost:5000` ya da
  `ngrok http 5000` çalıştırıp verdiği geçici adresi buraya yazman yeterli.
  Adres her yeniden başlatmada değişir, o zaman App ayarından güncellersin;
  gerçek domain (§3) geldiğinde tek yapılacak bu alanı değiştirmek.
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

## 9. Kalıcı Groq API anahtarı — 🟡

**Durum:** Bir anahtar verdin ve `.env`'de duruyor; **AI şu an gerçekten
çalışıyor.** Şema üretimi, netleştirme ajanı ve `/query/nl` gerçek bir modele
karşı uçtan uca doğrulandı.

**Sorun:** O anahtar **1 günlük bir denemeydi.** Süresi dolduğunda her AI
isteği 401 döner ve kullanıcı "şema üretilemedi" hatası alır — bu oturumda tam
olarak bu yaşandı ve teşhis etmek zaman aldı.

**Ne yapman lazım:** [console.groq.com/keys](https://console.groq.com/keys)
üzerinden kalıcı bir anahtar üret ve `.env`'deki `Groq__ApiKey=` satırını
güncelle. Sohbete yapıştırma gerekmez, doğrudan dosyaya yaz.

**Not:** Ücretsiz katmanın dakikalık token sınırı var. Sınıra takıldığında
sistem artık düzgün davranıyor — 500 değil, `Retry-After` başlığıyla **429**
dönüyor ve kullanıcıya "AI şu an meşgul, 24 saniye sonra dene" diyor.

---

### 9.1 Asıl mesele artık anahtarın süresi değil, KATMANI — 🔴

Bu oturumda üretimi **beş kereden fazla** deneyemedim; her seferinde Groq'un
ücretsiz katman duvarına çarptı. Önemli olan şu: **bizim kotamız doluydu**
(20.000 hakkın 15.000'i duruyordu), yine de `429` geldi. Yani sorun bizim
kodumuz ya da kotamız değil, Groq'un ücretsiz katmanı.

| | Ücretsiz | Developer (kart tanımlı) |
|---|---|---|
| Dakikalık token | ~6.000 | **~60.000** (10×) |
| Dakikalık istek | 30 | 300 |
| Token fiyatı | liste fiyatı | **%25 indirimli** |

**Faturalama modeli:** ön ödemeli kredi **yok**, aylık abonelik **yok**, sabit
ücret **yok**. Sadece kullandığın token. Fatura ay sonunda ya da kümülatif
kullanım $1/$10/$100 eşiklerini geçtikçe kesiliyor. Küçük kullanımda tipik
gider **$5-20/ay**.

**Yapılacak:** [console.groq.com](https://console.groq.com) → Billing → kart
ekle. Kod tarafında **hiçbir değişiklik gerekmiyor.**

**Neden başka sağlayıcıya geçmiyoruz:** DeepSeek'in en ucuz çıktı fiyatı
(off-peak $0,66/M) Groq'un ücretli çıktı fiyatından ($0,45/M) zaten pahalı ve
bizim iş yükümüz çıktı ağırlıklı. DeepSeek'in girdisi **bedava olsa bile**
kaybediyor. Üstelik geçiş, 8 metotlu bir `IAIService` implementasyonu yazmak
demek. Tam karşılaştırma: `second-phase/16-KOTA-VE-MALIYET.md`.

---

## 11-14. Namines Desk v2'nin açtığı kararlar

> Bu dördü diğerlerinden farklı bir kategoriydi: aşağıdaki üçü artık **karar
> verildi VE kodlandı** (Desk v2 tamamlandı — bkz.
> `namines_desk/11-DESK-V2-TAMAMLANDI.md`); 14. madde ise bilinçli olarak
> ertelendi. Kaynak: `namines_desk/10-DESK-V2-YOL-HARITASI.md`.

### 11. SSO devir jetonunun taşınma şekli — ✅ karar (b), kodlandı

**Seçilen: (b) kısa ömürlü `POST` formu** — en güvenli seçenek. Jeton hiçbir
zaman URL/query string'e girmiyor, dolayısıyla tarayıcı geçmişine ve
`Referer` başlığına düşmüyor. Uygulama: ana uygulamada gizli, otomatik
gönderilen bir `<form method="POST" target="_blank">`; Desk tarafında bunu
karşılayan bir Next.js Route Handler (`app/handoff/route.ts`). Jeton
tek kullanımlık (`DeskHandoffToken`, TeamInvite ile aynı desen — yalnızca
SHA256 hash'i saklanıyor), kısa ömürlü, ve eşzamanlı iki kullanım denemesinde
yalnızca birinin geçtiği `ExecuteUpdateAsync` tabanlı atomik bir güncellemeyle
garanti altına alındı (canlı Postgres'e karşı eşzamanlılık testiyle doğrulandı).

### 12. Toplu silme onay eşiği — ✅ 10 olarak onaylandı, kodlandı

Eşik **10** (senin onayınla). 10'dan az seçimde tarayıcının kendi `confirm()`'ü
yeterli; 10 ve üzerinde kullanıcı bir onay kutusuna **"SİL" yazmak zorunda**
(`BulkDeleteConfirm.tsx`) — Namines Bot'un "aprove/approve" tahmin ETMEME
kararıyla aynı ilke. Silme sunucuda TEK transactionda çalışıyor (ya hepsi ya
hiçbiri) — bir satırda FK ihlali olursa öncekiler de geri alınıyor, bu canlı
bir testle (gerçek FK ihlali zorlanarak) doğrulandı.

### 13. Desk'in oturum yolunda ham SQL'e izin — ✅ izin verildi, kodlandı

**Karar: izin verildi, tüm güvenlik önlemleriyle.** Uygulanan çok katmanlı
güvenlik:
1. Yalnızca proje **Owner**'ı erişebilir (sunucu `OrgRole.Owner` kontrolü).
2. Proje bazında **açıkça kapalı başlar** — Owner `AllowDeskSql` bayrağını
   bilerek açmadan konsol çalışmaz.
3. Yalnızca **tek bir salt-okunur ifade** (SELECT/WITH/EXPLAIN/SHOW) —
   regex tabanlı bir beyaz liste (`EnsureReadOnlySelectStatement`) INSERT/
   UPDATE/DELETE/DROP/ALTER/... ve `SELECT…INTO` dahil tüm yazma/DDL
   biçimlerini ve zincirlenmiş ifadeleri reddediyor.
4. Bağlantı, motor destekliyorsa (Postgres/MySQL/MariaDB) veritabanı
   düzeyinde salt-okunur bir oturumla açılıyor — regex'i atlatan bir ifade
   olsa bile veritabanının kendisi yazmayı reddediyor (bu doğrudan test
   edildi: gerçek bir `PostgresException` alındı). SQL Server/Oracle'da bu
   ikinci katman yok, o yüzden regex katmanı onlarda "ekstra" değil, tek
   savunma hattı.
5. Ayrı, sıkı bir rate-limit politikası (`sensitive`, 5 istek/dk).
6. Her çalıştırma denemesi audit trail'e yazılıyor.

### 14. Uyarı kuralları için e-posta altyapısı — 🟡 bilinçli olarak ertelendi

Senin açık talimatınla: **e-posta davet gönderme mekanizması değişecek, o
yüzden şimdilik olduğu gibi kalsın.** E5.3'ün "X tablosunda silme olursa
e-posta at" özelliği bu yüzden Desk v2'ye DAHİL EDİLMEDİ — ekip davet
e-postasıyla (35-KALAN-BUYUK-ISLER.md §8) aynı eksik altyapıya bağımlı
olmaya devam ediyor. Bir e-posta servisi (SendGrid/Postmark/SES gibi)
bağlandığında ikisi birlikte ele alınabilir.

---

## Kod dışı işler (bunlar da sende)

- [ ] `C:\Users\Enes Yel` dizinindeki yanlış git deposunu düzelt — remote'u
      `automated-recruitment-pipeline` görünüyor ve bu depoyla ilgisi yok.
- [ ] Ödeme altyapısı araştırması (Stripe TR sınırlı → Paddle / LemonSqueezy).
- [ ] `namines.com` alan adı + marka taraması.
- [ ] API'nin public adresi (webhook'un ulaşabilmesi için) — §3 ve §8 ile aynı iş.
