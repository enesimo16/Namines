# Namines Flow — kod incelemesi ve kapanış raporu

**Tarih:** 2026-09-20
**Kapsam:** `0070864..75e8040` (12 commit)
**Yöntem:** Bağımsız bir inceleme ajanı + kendi geçişim + çalışan yığında doğrulama

---

## 1. Bulunan ve kapatılan kusurlar

### 🔴 Yönlendirme üzerinden SSRF (bulan: bağımsız inceleme)

`SsrfGuard` yalnızca kullanıcının **yazdığı** URL'i doğruluyordu. `AutomationWebhook`
HTTP istemcisi yalnızca `Timeout` ile kayıtlıydı, yani varsayılan
`AllowAutoRedirect = true` yürürlükteydi.

Saldırı: saldırgan kendi public host'unu kaydeder (guard'dan geçer) → sunucusu
`302 Location: http://169.254.169.254/latest/meta-data/` döner → HttpClient
oraya **kullanıcının gövdesi ve başlıklarıyla** gider. "Şimdi test et" düğmesi
bunu anında ve kendi kendine yapılabilir hâle getiriyordu.

**Düzeltme:** `AllowAutoRedirect = false`; 3xx artık sebebiyle birlikte
"Skipped" olarak loglanıyor. Davranış testle sabitlendi
(`Yonlendirme_TAKIP_EDILMIYOR_ve_sebebi_yaziliyor`).

### 🔴 Sıralanan adımlarda veri bozulması (bulan: bağımsız inceleme)

Aksiyon ve koşul listeleri dizin numarasıyla anahtarlanıyordu, ama metin
kutuları kontrolsüz (`defaultValue` + `onBlur`). Bir adım yukarı taşındığında
React DOM düğümünü taşımıyor, kutularda eski metin kalıyor ve sonraki `onBlur`
o metni **yanlış adımın** yapılandırmasına yazıyordu.

**Düzeltme:** adımlara ve koşullara istemci tarafı `uid`. Tel biçimine
sızmıyor (API sınırında soyuluyor); şablonlar her örnekleme için yeni kimlik
üretiyor — `uid` bir örneğin kimliği, şablonun değil.

### 🟡 Hız sınırı log satırı sayıyordu (bulan: bağımsız inceleme)

Beş adımlı bir zincir **tek** testte dakikalık sınırı dolduruyordu; yalnızca
bildirim içeren bir kural ise hiç satır yazmadığı için **hiç** sınırlanmıyordu.

**Düzeltme:** satır sayısı sunucu tarafı adım sayısına bölünüyor. Sunucu adımı
olmayan kural bilerek sınırsız — sınırlanacak bir maliyet yok. Ayrıca test
yanıtı, çalıştırmadan hemen önce alınan bir zaman damgasıyla sınırlanıyor;
eskiden zincirde bildirim varsa eksik satırlar **önceki** bir testin
satırlarıyla doldurulabiliyordu.

### 🟡 İstemci eşleştiricisi fazla affediciydi (bulan: bağımsız inceleme)

`tableName`/`columnName` dışındaki her alan `columnType`'a düşüyor ve
"değerlendirilemez" diye affediliyordu. Sonuç: tablo olayına yazılmış bir
`columnName` koşulu tarayıcıda geçip sunucuda kalıyordu. Ayrıca ilişki
olaylarında istemci kaynak/hedeften tablo adı çözüyordu, sunucu ise bağlamı boş
bırakıyor.

**Düzeltme:** yalnızca `columnType` şüpheden yararlanıyor; ilişki olaylarında
bağlam boş. İki regresyon testi eklendi.

### 🟡 Görünmeyen ama canlı koşullar (bulan: bağımsız inceleme)

İlişki tetikleyicisine geçince koşul bölümü gizleniyor ama koşullar kayıtta
kalıyordu. Sunucu onları boş bağlamda değerlendirip kuralı **sessizce** ölü
hâle getiriyordu; ekranda sebep görünmüyordu.

**Düzeltme:** geçişte koşullar temizleniyor.

### 🟡 Başlık enjeksiyonu ve atılmayan yanıt (bulan: kendi geçişim)

Başlık **değerleri** şablondan geçiyor ve şablon **tablo adını** dolduruyor —
yani hedef sunucuya giden başlığa kullanıcının şema içeriği giriyor. Adında
CR/LF olan bir tablo enjeksiyon denemesi olurdu. Ayrıca `HttpResponseMessage`
atılmıyordu.

**Düzeltme:** başlık adı doğrulanıyor, değerden satır sonları atılıyor, yanıt
`using` ile atılıyor. Üçü de testli.

### 🟡 Saklama sayacı şişiyordu (bulan: bağımsız inceleme)

Yaş geçişi satırları yalnızca `Removed` işaretliyor, ardından kural-başına
geçiş **veritabanını** sorguluyordu — aynı satırlar iki kez sayılıyordu.

**Düzeltme:** yaş geçişinden sonra hemen kaydediliyor.

---

## 2. Doğrulanamayan bulgu

Bağımsız inceleme, `GetRules`'taki `GroupBy(...).Select(g => g.OrderByDescending(...).First())`
sorgusunun çevrilemeyeceğini ve ucun her çağrıda 500 döneceğini bildirdi.
**Üretilemedi:** sorgu hem Npgsql'de (çalışan yığında canlı doğrulandı) hem
SQLite'ta çalışıyor. Yine de çalışma kaydı **bulunan** bir projeyle yolu
yürüten bir test eklendi.

---

## 3. Temiz bulunan alanlar

- **Şablon motoru** (`AutomationTemplate.cs` + `naminesFlowTemplate.ts`) —
  tek geçiş gerçekten tek geçiş; `{{projectName}}` adlı bir tablo düz metin
  olarak çıkıyor, değer enjeksiyonu yapısal olarak imkânsız. İki uygulama
  birbirini yansıtıyor.
- **Migration `20260919221439`** — sıralama doğru: kopyalama eski sütunlar
  dururken yapılıyor, `SET DEFAULT` yeni eklemelerin geçmesini sağlıyor,
  `Down` bilerek kayıplı.
- **`useAutomationStore`** — `pendingCreates` yarış koruması sağlam.
- **`textToHeaders`** — ilk iki noktadan bölme doğru.

---

## 4. Bilerek açık bırakılanlar

| Konu | Neden şimdi değil |
|---|---|
| Eski `ActionType`/`ActionConfigJson` sütunlarının silinmesi | İleriye uyumluluk: kod geri alınırsa eski sürüm onları okuyor. **Ayrı bir sürümde, elle yazılmış migration ile.** `ef migrations add` üretmez. |
| Kuyruk dolduğunda kullanıcıya bildirim | Kuyruk düştüğünde hangi kuralların eşleşeceği daha hesaplanmamış — bir kurala atfedilemiyor. Sunucu logunda uyarı var. |
| `columnType` koşulunun istemcide değerlendirilmesi | Olay kolon tipini taşımıyor. Şüpheden yararlanıyor; sunucu doğru değerlendiriyor. |

---

## 5. Doğrulama durumu

| Katman | Durum |
|---|---|
| Backend testleri | **2040 geçiyor** |
| Frontend testleri | **184 geçiyor** |
| Tip kontrolü / tasarım kuralları / lint | temiz (0 hata) |
| Migration ileri + geri | geçici Postgres veritabanında doğrulandı |
| API uçtan uca | çalışan yığında doğrulandı (run log, test ucu, hız sınırı, Lint, Slack'te SSRF reddi) |
| **Arayüz, gerçek projede gözle** | **YAPILAMADI** — tarayıcı oturumu kimlik doğrulaması olmadan canvas'ta kalmıyor. Sizin oturumunuzda bir kez bakılmalı. |
