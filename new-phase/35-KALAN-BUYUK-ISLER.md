# 35 — Kalan Büyük İşler

> **Bu dosya "eksik kalanlar" listesi değil, sıradaki işler listesidir.**
> Aşağıdakilerin hiçbiri yarım bırakılmış bir iş değil; her biri, bilinçli
> olarak *sonraya* bırakılmış ayrı bir başlık. `CHECKLIST.md`'deki her G
> maddesinin sonundaki "**Kapsam dışı**" notlarının toplanmış hâli.
>
> Senin bir hesap/karar vermen gereken işler burada değil —
> onlar [34-SENDEN-BEKLENENLER.md](34-SENDEN-BEKLENENLER.md)'de.
>
> Son güncelleme: G43 (926 test yeşil).

---

## Bugüne kadar kapananlar

| İş | Durum |
|----|-------|
| §1 Console **yazma ekranları** | ✅ G40 — ekleme/düzenleme/onaylı silme, gerçek bir veritabanına karşı tarayıcıdan doğrulandı |
| §2 `import`, `/rpc`, `/query` | ✅ G41 — ayrı `CanExecuteSql` yetkisiyle, gerçek PostgreSQL'e karşı doğrulandı |
| §3 `identity` | ✅ G42 — altı motorda + NSL'de; anahtarı veritabanı mı atıyor, kullanıcı mı |
| §5 Bot'un **GitHub'a yazması** | ✅ G43 — kod tamam; çalışması için senin GitHub App'in gerekiyor |

Her biri kapanırken bölümünde **kalan alt maddeler** var; aşağıda duruyorlar.

---

## Öneri: hangi sırayla?

| Sıra | İş | Neden bu sırada |
|------|-----|-----------------|
| 1 | **§3 Kanonik JSON IR + model genişlemesi** | En pahalıya giden madde. Enum/`generated`/`collation`/şema adı bugün modelde yok ve üstüne yazılan her yeni üretici maliyeti artırıyor. `identity` (G42) bunun ilk parçasıydı ve tek başına iki gerçek hata ortaya çıkardı. |
| 2 | **§1 Console RBAC + denetim kaydı** | Panel artık **yazıyor**. Yazan ama kimin ne yaptığını kaydetmeyen bir panel, üretimde kullanılamaz — bu ikisi artık "güzel olur" değil, ön koşul. |
| 3 | **§2 GraphQL** | Kendi başına bir iş (şema üretimi + resolver'lar + N+1). Model genişlemesinden SONRA yapılmalı, yoksa iki kez yazılır. |
| 4 | **§5 Bot'un kalanı** | PR'da önizleme veritabanı + `/namines` komutlarının gerçekten çalışması. Kimlik bilgilerin geldikten sonra anlamlı. |
| 5 | **§4, §6, §7** | Dış servis/içerik ağırlıklı; kod tarafı en hafif olanlar. |

> **§2 `/query/nl` ve §1 doğal dil sorgu aynı işin iki ucu.** İkisi de "üretilen
> SQL otomatik çalıştırılsın mı?" sorusuna cevap istiyor. Birlikte tasarlanmalı,
> ayrı ayrı değil — yoksa aynı güvenlik kararı iki farklı yerde iki farklı
> biçimde verilir.

---

## 1. Console: yazma ekranları ve yönetim ([07](07-CONSOLE-ADMIN-UI.md))

**Yazma ekranları G40'ta tamamlandı** — ekleme, düzenleme ve onaylı silme,
gerçek bir PostgreSQL'e karşı tarayıcıdan doğrulandı. Kalanlar:

> ⚠️ **İlk ikisi artık "güzel olur" değil, ön koşul.** Panel yazıyor; kimin ne
> yaptığını kaydetmeyen ve herkese aynı yetkiyi veren bir yazma paneli üretimde
> kullanılamaz. Yazma eklendiği anda bu iki madde borç hâline geldi.

- **Console RBAC** (§4) — son kullanıcı rolleri.
- **Denetim kaydı** (§5) — konsoldan yapılan değişikliklerin izi.
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

- **`/query/nl`** — doğal dil sorgu. AI katmanı bağlantısı ve üretilen SQL'in
  otomatik çalıştırılıp çalıştırılmayacağı ayrı bir güvenlik tasarımı ister.
- **GraphQL** (§3) — şema üretimi + resolver'lar + N+1; kendi başına bir iş.
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

**G42'de eklendi:** `identity` — anahtarı veritabanı mı atıyor, kullanıcı mı.
Altı motorda ve NSL'de karşılığıyla birlikte.

- **Kanonik JSON IR** (§3) — ⚠️ **En büyük ve en riskli madde.** Doküman;
  enum, şema (`public`), `generated`, `collation`, dizi tipi, `@ui`, `@tag`,
  kısmi/kapsayıcı index gibi bugünkü `DatabaseSchema`'da **hiç bulunmayan**
  alanlar tanımlıyor (`identity` G42'de eklendi). Bunu yapmak, çekirdek modeli
  genişletip dalgayı 18 üretici, 6 DDL motoru, frontend ve 900+ testin üstünden
  geçirmek demek. "Kalan detay" değil, temel bir dönüşüm.
  > **G42 bunun neden ertelenemeyeceğini gösterdi:** tek bir alan (`identity`)
  > eklemek iki gerçek hata ortaya çıkardı — PostgreSQL ara tablonun iki
  > yabancı anahtarına da `SERIAL` veriyordu, ve NSL ayrıştırıcısı her
  > ayrıştırmada rastgele kimlik üretiyordu. Alanlar eksik olduğu sürece bu tür
  > hatalar görünmüyor.
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

## Bilerek yapılmayacaklar

Aşağıdakiler "sonra" listesinde değil, **hayır** listesinde. Gerekçeleri
[32-DEFERRED-NOT-REJECTED.md](32-DEFERRED-NOT-REJECTED.md)'de:

- `docker.sock`'un container'a mount edilmesi — host'ta root eşdeğeri yetki verir.
- Bilinmeyen bir bot komutunun "en yakın"a çevrilmesi (`aprove` → `approve`).
- Bir motorun desteklemediği referans fiilinde `CASCADE`'e düşmek.
