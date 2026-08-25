# 34 — Senden Beklenenler

> Bu dosya, **kodun hazır olduğu ama senin bir hesap, bir sayı ya da bir karar
> vermen gerektiği için tamamlanamayan** işleri toplar. Hiçbiri diğer işleri
> bloke etmiyor — her biri geldiğinde ilgili yer birkaç saatte bağlanır.
>
> Kaynak: `CHECKLIST.md` → "Kodun beklediği kararlar/erişimler". Burası aynı
> listenin basit dille yazılmış ve tek başına okunabilir hâli.
>
> **Son güncelleme:** G52 (Team planı, NAI v1, gelişmiş ayarlar). 1136 test yeşil.

**Genel kural:** Hiçbir API anahtarını, parolayı ya da token'ı sohbete
yapıştırma. Hepsi ortam değişkenine (`.env`) girer; sen sadece "aldım" de yeter.
`.env` `.gitignore`'da, depoya hiç girmiyor.

## Önce sade hâli (teknik bilgi gerekmez)

Aşağıdakilerin **hiçbiri kod eksikliği değil.** Hepsinin kodu yazıldı ve test
edildi; eksik olan tek şey senin bir hesap açman, bir sayı söylemen ya da bir
karar vermen.

### 🔴 Bu ikisi ürünü şu an tutuyor

**A) Disk alanı — 3,8 GB kaldı**
Geçen sefer 4,8 GB'tı, **daha da azaldı.** Bu oturumda veritabanı container'ı
yine düştü ve elle başlatmam gerekti. Bu, kod yazmayı da yavaşlatıyor.
→ En acil madde bu. §10

**B) Stripe hesabı ve iki fiyat**
Ödeme kodu tamamen hazır: Pro 7,5$/ay ve Team 20$/ay, checkout, webhook,
plan ayrımı, iptal, portal — hepsi yazıldı ve test edildi. **Ama tek kuruş
tahsil edemez**, çünkü Stripe'ta bu iki fiyatın karşılığı yok.
→ Stripe'ta iki fiyat oluştur, iki `price_...` kimliğini `.env`'e koy. §6

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

**F) Kalıcı Groq anahtarı** — Verdiğin anahtar **1 günlük denemeydi.** Şu an
`.env`'de duruyor ve çalışıyor ama süresi dolduğunda şema üretimi 401 verecek.
Kalıcı bir anahtar lazım. §9

### 🟢 Bunlar sadece "onaylıyor musun?"

**G) Üç rate limit sayısı** — Artık **boş değil**, ben makul varsayılanlar
koydum: Free 60, Pro 600, Team 3.000 istek/dakika. Sana yüksek ya da düşük
geliyorsa söyle, tek satır. §4

**H) Redis: evet mi hayır mı?** — Tek kelime yeter. Şu an istek sayacı tek
sunucunun hafızasında; ikinci bir sunucu açarsan aynı kişi iki katı hak
kazanır. Tek sunucuda çalıştığın sürece sorun değil. §5

**I) İki küçük teknik onay** — Dokümandan bilerek saptığım iki nokta. Kabul
ediyorsan hiçbir şey yapman gerekmiyor. §7

**J) Neon hesabı** — Branch veritabanları şu an container ile açılıyor;
çalışıyor ama yavaş. Neon anında yapıyor. Tamamen isteğe bağlı. §1

---

## ✅ Kapananlar (artık bir şey yapman gerekmiyor)

| Ne | Nasıl kapandı |
|----|---------------|
| **Groq anahtarı yokluğu** | Anahtar `.env`'de; şema üretimi ve netleştirme ajanı gerçek bir modele karşı uçtan uca doğrulandı. ⚠️ Ama anahtar 1 günlük — bkz. F |
| **`/query/nl` hiç denenmemişti** | Groq bağlandı, gerçek üretim yapıldı |
| **Plan başına rate limit sayısı yoktu** | Varsayılanlar kondu (60/600/3.000/10.000); artık kararın değil onayın bekleniyor |
| **Team/Enterprise ayrımı yapılamıyordu** | `PlanCode` alanı eklendi; webhook Stripe fiyatından planı okuyor. Kalan tek şey fiyat kimlikleri |
| **Fiyatlar belirsizdi** | Karar verildi: Free 0$, Pro 7,5$/ay, Team 20$/ay (3 koltuk) |
| **Geliştirici hesabı sürekli unutuluyordu** | `.env`'den açılışta kendiliğinden kurulan, sınırsız, görünmeyen Dev hesabı |

---

## Tek bakışta

| # | Ne | Tipi | Aciliyet | Bunsuz ne olmuyor |
|---|----|------|----------|-------------------|
| 10 | **Disk alanı** (şu an 3,8 GB) | makine | 🔴 | Container'lar düşüyor, geliştirme yavaşlıyor |
| 6 | **Stripe hesabı + 2 fiyat kimliği** | hesap | 🔴 | **Ödeme kodu hazır ama tek kuruş tahsil edemiyor** |
| 8 | GitHub App (`AppId`, `PrivateKey`, `WebhookSecret`) | hesap | 🟡 | Bot hazır ama PR'a tek satır yazamıyor |
| 2 | npm yayını | hesap | 🟡 | MCP `npx` ile kurulamıyor; kullanıcı depoyu klonlamak zorunda |
| 3 | Public alan adı (`api.namines.com`?) | karar | 🟡 | Eject edilen SDK nereye bağlanacağını bilmiyor; webhook adresi de buna bağlı |
| 9 | **Kalıcı** Groq anahtarı | hesap | 🟡 | Mevcut anahtar 1 günlük deneme — dolunca AI tamamen durur |
| 4 | Rate limit sayıları | onay | 🟢 | Varsayılan kondu; sadece onayın/itirazın bekleniyor |
| 5 | Redis: evet / hayır | karar | 🟢 | Tek sunucuda sorun yok; 2+ sunucuda limit anlamını kaybediyor |
| 7 | İki doküman sapmasının onayı | onay | 🟢 | Hiçbir şey bloke değil; itirazın varsa geri alırım |
| 1 | Neon hesabı | hesap | 🟢 | Branch DB'ler yavaş açılıyor (ama açılıyor) |

**En yüksek etkili ikisi:** **10** (disk — her şeyi yavaşlatıyor) ve **6**
(Stripe — ürünün para kazanmasının önündeki tek engel).

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

## 4. Plan başına rate limit sayıları — 🟢 artık sadece onay

**Durum değişti:** Eskiden kodda okunacak sayı yoktu. Artık
[`PlanQuotas`](../backend/Namines.Core/Analysis/PlanQuotas.cs)'ta duruyorlar:

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

## 5. Redis kararı (evet / hayır)

**Ne yapman lazım:** Sadece "kullanacağız" ya da "kullanmayacağız" de.

**Neden:** Şu an istek sayacı **tek sunucunun belleğinde**. İki API instance'ı
açarsan aynı kullanıcı iki katı hak kazanır — limit sessizce anlamını kaybeder.
Ayrıca [08 §6](08-GATEWAY-API.md)'daki metadata cache de buna bağlı.

**Not:** SignalR backplane'i için Redis zaten destekleniyor (G6), yani altyapı
tamamen yabancı değil.

---

## 6. Stripe hesabı + iki fiyat kimliği — 🔴 ACİL

**Ne yapman lazım:**
1. Stripe hesabı aç (yoksa).
2. İki **fiyat (price)** oluştur:
   - **Pro** — aylık **7,50 $**
   - **Team** — aylık **20,00 $**
3. Üç değeri `.env`'e koy:
   ```
   Stripe__SecretKey=sk_...
   Stripe__ProPriceId=price_...
   Stripe__TeamPriceId=price_...
   ```
   Ayrıca webhook için `Stripe__WebhookSecret=whsec_...`

**Neden acil:** Ödeme tarafının **kodu tamamen bitti** — checkout iki planı da
biliyor (`?plan=pro|team`), webhook hangi fiyatın ödendiğini okuyup kullanıcının
planını yazıyor (`PlanCode`), iptal Free'ye düşürüyor, portal çalışıyor,
kullanıcı ayarlarda üç plan kartını görüyor. **Tek eksik, Stripe tarafında bu
fiyatların var olmaması.** Yani ürün bugün satış yapamıyor ve bunun sebebi kod
değil.

**Fiyatı kodda tutmuyoruz, yalnızca kimliğini:** fiyatı değiştirmek
istediğinde Stripe panelinden değiştirirsin, yeniden dağıtım gerekmez.

**Ayrıca:** Stripe Türkiye'de sınırlı — Paddle / LemonSqueezy araştırması hâlâ
açık bir madde (aşağıdaki "kod dışı işler"e bak). Kod tarafı Stripe'a yazıldı;
başka bir sağlayıcıya geçersen webhook ve checkout değişir, plan modeli aynı kalır.

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

## 10. Disk alanı — 🔴 EN ACİL

**Durum kötüleşti:** Geçen sefer 4,8 GB'tı, şu an **3,8 GB**.

**Ne yapman lazım:** C: sürücüsünde yer aç. En hızlı kazanç sırasıyla:

1. **WSL2 sanal diski sıkıştır** — Docker sildiğin şeyi geri veriyor ama VHDX
   kendiliğinden küçülmüyor. PowerShell'de (yönetici):
   ```
   wsl --shutdown
   Optimize-VHD -Path "$env:LOCALAPPDATA\Docker\wsl\disk\docker_data.vhdx" -Mode Full
   ```
   (`Optimize-VHD` yoksa Hyper-V modülü kurulu değildir; `diskpart` ile
   `compact vdisk` de aynı işi yapar.)
2. **Kullanılmayan Docker imajları** — `docker system prune -a --volumes` ⚠️
   `--volumes` control DB'yi de siler; onsuz çalıştır.
3. **Eski `bin/` ve `obj/` klasörleri** — `dotnet clean` ya da elle silme.

**Neden acil:** Bu oturumda control DB container'ı yine düştü ve elle
başlatmam gerekti. CLAUDE.md zaten uyarıyor: *"Docker/build hataları illa kod
hatası değil — önce boş alanı kontrol et."* Bu, hem ürünü hem geliştirmeyi
yavaşlatıyor.

---

## Kod dışı işler (bunlar da sende)

- [ ] `C:\Users\Enes Yel` dizinindeki yanlış git deposunu düzelt — remote'u
      `automated-recruitment-pipeline` görünüyor ve bu depoyla ilgisi yok.
- [ ] Ödeme altyapısı araştırması (Stripe TR sınırlı → Paddle / LemonSqueezy).
- [ ] `namines.com` alan adı + marka taraması.
- [ ] API'nin public adresi (webhook'un ulaşabilmesi için) — §3 ve §8 ile aynı iş.
