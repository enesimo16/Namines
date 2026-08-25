# 35 — Kalan Büyük İşler

> **Bu dosya "eksik kalanlar" listesi değil, sıradaki işler listesidir.**
> Aşağıdakilerin hiçbiri yarım bırakılmış bir iş değil; her biri, bilinçli
> olarak *sonraya* bırakılmış ayrı bir başlık. `CHECKLIST.md`'deki her G
> maddesinin sonundaki "**Kapsam dışı**" notlarının toplanmış hâli.
>
> Senin bir hesap/karar vermen gereken işler burada değil —
> onlar [34-SENDEN-BEKLENENLER.md](34-SENDEN-BEKLENENLER.md)'de.
>
> Son güncelleme: **G52** (Team planı, NAI v1 modelleri, çalışan gelişmiş
> ayarlar). **1136 test yeşil.**

---

## Önce sade hâli (teknik bilgi gerekmez)

### Ürün şu an ne yapabiliyor?

Bir kullanıcı Namines'e girdiğinde artık şunları yapabiliyor:

1. **Şemayı tasarlıyor** — canvas'ta tablo çiziyor, ya da AI'ya tarif ediyor,
   ya da var olan bir veritabanından çekiyor.
2. **Altı farklı veritabanına derliyor** — PostgreSQL, SQL Server, MySQL,
   MariaDB, Oracle, SQLite. Bir motorun desteklemediği bir şey varsa uyduruk
   çıktı üretmiyor, "bu motorda olmaz" diyor.
3. **Kod indiriyor** — 19 farklı hedef: TypeScript tipleri, Prisma, Drizzle,
   Django, hazır bir yönetim paneli, hatta şemanın kendi metin biçimi.
4. **Canlı veritabanına bağlanıyor** — API üzerinden veri okuyup yazıyor,
   toplu veri yüklüyor, ham SQL çalıştırıyor (ayrı izinle).
5. **Üretilen panelden veri düzenliyor** — satır ekliyor, düzenliyor, onaylı
   siliyor. Kimin ne yaptığı kayda geçiyor.
6. **Değişikliği inceletiyor** — "bu değişiklik veri kaybettirir mi" sorusunu
   cevaplayan bir motor var; PR'lara yorum yazacak bot da hazır.

### Peki ne kaldı?

Kalanlar **eksik parça değil, sıradaki başlıklar.** Hiçbiri yarım bırakılmış bir
iş değil; her biri kendi başına ayrı bir proje.

**1. Bot'un işini bitirmesi** — Bot şu an PR'a yorum yazabiliyor. Kalan: PR
açılınca otomatik bir *deneme veritabanı* kurup "değişikliği burada dene" demesi,
ve `/namines plan` gibi komutların gerçekten çalışması. *Senin GitHub App'in
geldiği an anlamlı hâle gelir.*

**2. GraphQL** — Şu an API'ye REST ile bağlanılıyor. Bazı geliştiriciler GraphQL
tercih ediyor. Ayrı bir kütüphane ve mimari karar gerektiriyor; **Redis
kararına bağlı** (34 §5).

**3. Şemanın kalan detayları** — View, satır seviyesi güvenlik, arayüz etiketleri
gibi ileri seviye şey. En pahalı kısım (enum, hesaplanan kolon, collation)
geçildi; kalanlar daha küçük.

**4. Panelin kalan özellikleri** — Grafik/dashboard, doğal dil sorgu, üretilen
paneli özelleştirme.

**5. Ekibin derinleşmesi** *(yeni)* — 3 koltuk, davet bağlantısı ve ortak
workspace kuruldu. Kalan: aynı şema üzerinde **canlı birlikte düzenleme**
(SignalR altyapısı hazır ama ekip modeline bağlanmadı), rol bazlı proje
kısıtları ve ekip içi bildirimler.

**6. Faturalamanın canlıya çıkması** *(yeni)* — Kod hazır, Stripe'ta iki fiyat
oluşturulması bekliyor (34 §6). Sonrasında kalan: başarısız ödeme akışı,
fatura geçmişi ekranı, plan yükseltme/düşürme kenar durumları.

**7. Altyapı ve içerik** — Otomatik yedekleme, hata izleme panelleri, SEO için
100+ hazır şema. Çoğu kod değil, kurulum ve içerik işi.

### Ne yapılmayacak?

Üç şey **bilerek reddedildi**, ertelenmedi:

- Docker soketini container'a bağlamak — bilgisayarında root yetkisi vermek demek.
- Bot'un yanlış yazılmış bir komutu "tahmin etmesi" (`aprove` → `approve`) —
  yıkıcı bir değişikliğin yazım hatasıyla onaylanması demek.
- Bir motorun desteklemediği silme davranışında `CASCADE`'e düşmek — sessiz veri
  kaybı demek.

---

## Bugüne kadar kapananlar

| İş | Durum |
|----|-------|
| §1 Console **yazma ekranları** | ✅ G40 — ekleme/düzenleme/onaylı silme, gerçek bir veritabanına karşı tarayıcıdan doğrulandı |
| §2 `import`, `/rpc`, `/query` | ✅ G41 — ayrı `CanExecuteSql` yetkisiyle, gerçek PostgreSQL'e karşı doğrulandı |
| §3 `identity` | ✅ G42 — altı motorda + NSL'de; anahtarı veritabanı mı atıyor, kullanıcı mı |
| §5 Bot'un **GitHub'a yazması** | ✅ G43 — kod tamam; çalışması için senin GitHub App'in gerekiyor |
| §3 **enum** | ✅ G44 — altı motorda; karşılığı olmayan motorda CHECK'e düşüyor, kısıt kaybolmuyor |
| §3 `generated`, `collation`, **dizi** | ✅ G45 — gerçek PostgreSQL ve SQLite'ta doğrulandı |
| §3 **kanonik JSON IR** | ✅ G46 — sürümlü, çift yönlü, `ir.json` eject hedefi |
| §1 **RBAC + denetim kaydı** | ✅ G47 — kayıt Gateway'de (atlatılamaz), panelin varsayılan rolü salt-okunur |
| §2 `/query/nl` | ✅ G48 — varsayılan çalıştırmaz; `execute` verilse bile yalnızca okuma. ⚠️ Groq anahtarı olmadığı için **mutlu yolu hiç denenmedi** |
| §1 Studio'nun yeni alanları görmesi | ✅ G49 — enum, identity, hesaplanan kolon, collation, dizi artık canvas'ta |
| **Netleştirme ajanı + NAI modeli** | ✅ G51 — 14 iş türü, en fazla 5 soru (sıfır token), üç kendi modelimiz, plan bazlı indirgeme |
| **Kota modeli gerçekten uygulanıyor** | ✅ G51 — plan başına günlük token, paylaşılan havuz, TR gün sınırı, 429 + `Retry-After` |
| **Görünmeyen Dev hesabı** | ✅ G50 — `.env`'den açılışta kurulan, sınırsız, Stripe'ın ezemediği ayrı bayrak |
| **Pro/Team fiyatlandırma + ödeme kodu** | ✅ G50 — 7,5$/20$, `PlanCode`, webhook plan ayrımı. ⚠️ Stripe'ta fiyat kaydı bekliyor (34 §6) |
| **Team: koltuk, davet linki, ortak workspace** | ✅ G52 — 3 koltuk, tek kullanımlık link, ekip paneli, paylaşılan projeler |
| **Gelişmiş AI ayarları** | ✅ G52 — 11 ayar artık gerçekten okunuyor (eskiden yalnızca localStorage'daydı, tamamı süstü) |

Her biri kapanırken bölümünde **kalan alt maddeler** var; aşağıda duruyorlar.

---

## Öneri: hangi sırayla?

> **Not:** İlk iki sıra artık kod işi değil, **senin bir hesap açmana** bağlı
> (bkz. [34](34-SENDEN-BEKLENENLER.md)). Kod tarafı her ikisinde de bitti.

| Sıra | İş | Neden bu sırada |
|------|-----|-----------------|
| 0 | **Stripe'ta iki fiyat** | Ödeme kodunun tamamı hazır ve test edildi; ürün bugün satış yapamıyor ve sebebi kod değil. En küçük iş, en büyük etki. |
| 0 | **Disk açmak** | 3,8 GB kaldı; container'lar düşüyor ve her şeyi yavaşlatıyor. |
| 1 | **§5 Bot'un kalanı** | PR'da önizleme veritabanı + `/namines` komutları. GitHub App'in geldiği an anlamlı hâle gelir ve bot zaten yazabiliyor. |
| 2 | **Ekibin derinleşmesi** | Koltuk/davet/ortak workspace kuruldu; sıradaki doğal adım aynı şema üzerinde canlı birlikte düzenleme. SignalR altyapısı zaten var, ekip modeline bağlanması gerekiyor. |
| 3 | **§2 GraphQL** | Bir GraphQL motoru bağımlılığı + proje başına şema önbelleği ister; ikincisi Redis kararına bağlı. |
| 4 | **§3'ün kalanı** | Şema adı (`public`), `@ui`/`@tag`, view, RLS, Migration IR, WASM. En pahalı kısım (enum/generated/collation/dizi) geçildi. |
| 5 | **§1'in kalanı** | Dashboard motoru, doğal dil sorgu, özelleştirme katmanı, kolon maskeleme/satır filtresinin role bağlanması. Bir de **enum'ları arayüzden tanımlama**. |
| 6 | **§4, §6, §7** | Dış servis/içerik ağırlıklı; kod tarafı en hafif olanlar. |

> **§2 `/query/nl` ve §1 doğal dil sorgu aynı işin iki ucu.** İkisi de "üretilen
> SQL otomatik çalıştırılsın mı?" sorusuna cevap istiyor. Birlikte tasarlanmalı,
> ayrı ayrı değil — yoksa aynı güvenlik kararı iki farklı yerde iki farklı
> biçimde verilir.

---

## 1. Console: yazma ekranları ve yönetim ([07](07-CONSOLE-ADMIN-UI.md))

**Yazma ekranları G40'ta tamamlandı** — ekleme, düzenleme ve onaylı silme,
gerçek bir PostgreSQL'e karşı tarayıcıdan doğrulandı. Kalanlar:

**G47'de tamamlandı:** Console RBAC (§4) ve denetim kaydı (§5). Kayıt Gateway'de
tutuluyor, yani panelden atlatılamaz; panelin varsayılan rolü salt-okunur.

Kalanlar:
- **Dashboard motoru** (§6).
- **Doğal dilde sorgu** (§7).
- **Özelleştirme katmanı** (§9) — üretilen konsolun üzerine yazmadan
  özelleştirilmesi.
- **Diğer konsol hedefleri:** React+Vite, Blazor, Retool JSON.

**Büyüklük:** Büyük. Tek başına birkaç oturum.

---

## 2. Gateway'in kalan sorgu yüzeyi ([08](08-GATEWAY-API.md))

Bugün var: `list`, `detail`, `create`, `update`, `delete`, `export`,
`tables`, `openapi.json` — anahtar/izin modeli, CIDR ve origin kısıtları,
rate limit dahil, hepsi canlı doğrulanmış.

**G41'de eklendi:** `import` (tek işlem, ya hepsi ya hiçbiri), `/rpc`,
`/query` (ayrı `CanExecuteSql` yetkisiyle) — hepsi gerçek PostgreSQL'e karşı
doğrulandı.

Kalanlar:

**G48'de eklendi:** `/query/nl` — varsayılan olarak SQL'i döndürür, çalıştırmaz;
`execute: true` verilse bile yalnızca okuma sorguları çalışır.

- **GraphQL** (§3) — şema üretimi + resolver'lar + N+1; kendi başına bir iş.
  ⚠️ Bir GraphQL motoru bağımlılığı ve **proje başına şema önbelleği** ister;
  ikincisi [34](34-SENDEN-BEKLENENLER.md) §5'teki Redis kararına bağlı.
- **`/realtime`** (§9, doküman zaten P2 diyor).
- **Metadata cache** (§6) — **Redis kararına bağlı**, bkz. [34](34-SENDEN-BEKLENENLER.md) §5.
- **`expand` (ilişki gömme)** — ⚠️ *bilinçli olarak reddedildi, ertelenmedi.*
  Çalışma zamanında **şema** bilgisi ister; Gateway ise durumsuz (bağlantı
  dizesi istek başına geliyor). Anahtar yolunda proje biliniyor, oturum yolunda
  bilinmiyor. Yalnızca bir kimlik yolunda çalışan bir özellik, hiç olmamasından
  kötüdür. Bunu açmak, önce durumsuzluk kararını değiştirmek demek.

**Büyüklük:** Orta-büyük. Parçalara bölünebilir; `import` ve `/rpc` en küçüğü.

---

## 3. NSL'in kalanı ([04](04-NSL-SCHEMA-IR.md))

Bugün var: metin biçimi, ayrıştırıcı, doğrulayıcı (25 kuralın modelde karşılığı
olan 15'i), çift yönlü `nsl` eject hedefi.

**G42-G46'da eklendi:** `identity`, enum, `generated`, `collation`, dizi tipi ve
kanonik JSON IR — hepsi altı motorda, NSL sözdiziminde ve `ir.json` hedefinde.
Gerçek PostgreSQL ve SQLite'a karşı doğrulandı.

- **Şema adı (`public`), `@ui`, `@tag`, kısmi/kapsayıcı index** — modelde hâlâ yok.
  > **Bu genişlemenin neden ertelenemeyeceği ölçüldü:** eklenen her alan gerçek
  > bir hata ortaya çıkardı — `identity` PostgreSQL'in ara tablonun iki yabancı
  > anahtarına da `SERIAL` verdiğini ve NSL ayrıştırıcısının her ayrıştırmada
  > rastgele kimlik ürettiğini; enum Türkçe kültürdeki `ToUpper` hatasını;
  > `generated` PostgreSQL'in tipi zorunlu kıldığını. Alanlar eksik olduğu sürece
  > bu tür hatalar görünmüyor.
- **Enum / view / RLS / `@ui` / `@tag` sözdizimi** — yukarıdaki modele bağlı;
  bugün model bunları taşımıyor, o yüzden sözdizimi yazmak hiç tetiklenmeyecek
  bir kuralı "var" göstermek olurdu.
- **Migration IR** (§7).
- **WASM derlemesi** (§10 / §1 hedef #7) — tarayıcıda anlık önizleme.

**Sırası:** Model genişlemesi ne kadar geç yapılırsa o kadar pahalı — üstüne
yazılan her yeni üretici maliyeti artırıyor.

---

## 4. Data plane: yönetilen altyapı ([06](06-DATA-PLANE.md))

Bugün var: branch başına gerçek bir yerel PostgreSQL veritabanı
(**docker.sock mount edilmeden**), TTL, rastgele parola, gerçek bağlantıyla
hazırlık kontrolü.

- **Neon copy-on-write branch'leri** (§3) — [34](34-SENDEN-BEKLENENLER.md) §1'i bekliyor.
- **MinIO/S3'e yedek** (§9).
- **Namines Bridge** (§6) — on-prem tünel agent'ı.
- **PII maskeleme** (§4) — *not: log tarafındaki PII maskeleme (21) yapıldı;
  bu, veri düzlemindeki ayrı bir iş.*
- **Plan bazlı kotalar** (§10).
- **Vault ile kimlik saklama** (§5).

> ⚠️ Bugünkü sağlayıcı **yerel geliştirme veritabanı** üretir. Prod verisi için
> değildir ve öyle sunulmamalı.

---

## 5. Namines Bot'un GitHub'a yazması ([11](11-MIGRATIONS-BRANCHING.md) §7)

Bugün var: HMAC-SHA256 imza doğrulama, `/namines` komut ayrıştırıcı, PR yorumu
ve status check metni üreten `PullRequestReviewComposer` — hepsi test edilmiş.

**G43'te yazma tarafı tamamlandı:** App kimlik doğrulaması, yorum yazma, status
check oluşturma, `.nsl` okuma ve kırılma analizinin PR'a bağlanması. Sahte bir
HTTP katmanına karşı uçtan uca test edilmiş durumda.

⚠️ **Çalışması için `Github:AppId` ve `Github:PrivateKey` gerekiyor**
(bkz. [34](34-SENDEN-BEKLENENLER.md)). Kimlik bilgisi yoksa bot yazmayı denemez.

Kalanlar:

- PR açılınca **önizleme veritabanı** provision etme.
- `.nsl` senkron PR'ı, tip senkronu.
- `/namines plan|preview|approve` komutlarının gerçekten çalışması (şu an
  tanınıyor ve "henüz yok" cevabı veriyor).

> Sahte bir istemciyle "yazıyormuş gibi" yapılmadı: çalıştığı sanılan ama
> hiçbir şey yapmayan bir özellik, hiç olmayandan kötüdür.

---

## 6. Gözlemlenebilirlik ve faturalama kuyruğu ([21](21-OBSERVABILITY.md), [22](22-BUSINESS-MODEL.md))

Bugün var: Prometheus metrikleri (RED + iş metrikleri, canlı doğrulandı),
Serilog PII maskeleme, kullanım ölçümü (AI çağrısı, API isteği, branch DB).

- Loki / Tempo / Grafana / Sentry / PostHog / ClickHouse — **kod değil,
  yapılandırma ve dış servis.**
- Alert kuralları (§5), dashboard'lar (§6), SLO'lar (§7).
- Stripe'a fatura kalemi gönderme — [34](34-SENDEN-BEKLENENLER.md) §6'ya bağlı.
- Depolama/transfer ölçümünün gerçek kaynaklara bağlanması.

> OTLP exporter **bilerek çıkarıldı**: 1.9–1.12 sürümlerinde NU1902 güvenlik
> uyarısı var ve toplayacak bir collector zaten yok. Prometheus kaldı.

---

## 7. Blueprint Hub ([23](23-GTM.md) §2 Döngü 5)

`namines.com/hub/ecommerce-schema` gibi SEO sayfaları; Faz 1'deki 5 şablon →
100+ blueprint.

**Bu iş çoğunlukla kod değil, içerik.** Sayfa mekanizması (paylaşım sayfası,
sosyal önizleme, sitemap, meta etiketler) **G37'de tamamlandı**; eksik olan
100+ şemayı yazmak ve alan adı ([34](34-SENDEN-BEKLENENLER.md) §3).

---

## 8. Ekibin derinleşmesi ([05](05-CONTROL-PLANE.md) §6) — *yeni başlık*

**G52'de kurulan temel:** Team planı 3 koltuk (satın alan + 2 davet), tek
kullanımlık davet bağlantısı, ortak workspace (üyeler birbirinin projelerini
görüyor), ekip paneli ve "kim ne zaman ne değiştirdi" listesi.

**Kalanlar:**

- **Aynı şema üzerinde canlı birlikte düzenleme.** SignalR altyapısı G6'da
  kuruldu ve `CanvasHub`'ın odası G17'de sunucu-otoriteli branch'e bağlandı —
  yani teknik temel hazır. Eksik olan, ekip üyeliğinin bu odalara yetki kaynağı
  olarak bağlanması ve imleç/seçim paylaşımı.
- **Rol bazlı proje kısıtları.** `OrgRole` (Viewer/Editor/Admin/Owner) tanımlı
  ve change request akışında uygulanıyor, ama Studio arayüzü henüz rolü dikkate
  almıyor: Viewer da düzenleme araçlarını görüyor. Sunucu reddediyor, yani
  güvenlik açığı değil — ama kullanıcıya yapamayacağı şeyi göstermek kötü.
- **Ekip içi bildirim.** "X seni ekibe ekledi", "Y şemayı değiştirdi" gibi.
  Bugün kullanıcı ancak paneli açarsa görüyor.
- **Davet e-postası.** Şu an bağlantıyı kopyalayıp kendin gönderiyorsun.
  E-posta servisi bağlandığında doğrudan gönderilebilir. *Bilerek böyle:*
  e-posta altyapısı yokken "davet gönderildi" demek, gitmemiş bir davetin
  gittiğini sandırırdı.
- **Koltuk sayısını plana göre satın alma.** Bugün Team sabit 3 koltuk. Ek
  koltuk satmak, Stripe tarafında adet (quantity) bazlı fiyatlandırma ister.

**Neden şimdi mantıklı bir sıra:** Ürünün "birlikte çalışma" tarafı artık
satılabilir durumda; canlı düzenleme, o vaadi tamamlayan tek büyük parça.

---

## Bilerek yapılmayacaklar

Aşağıdakiler "sonra" listesinde değil, **hayır** listesinde. Gerekçeleri
[32-DEFERRED-NOT-REJECTED.md](32-DEFERRED-NOT-REJECTED.md)'de:

- `docker.sock`'un container'a mount edilmesi — host'ta root eşdeğeri yetki verir.
- Bilinmeyen bir bot komutunun "en yakın"a çevrilmesi (`aprove` → `approve`).
- Bir motorun desteklemediği referans fiilinde `CASCADE`'e düşmek.
