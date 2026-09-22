# 01 — Sıradaki İşler

> Ne yapacağız, hangi sırayla ve **neden o sırayla**. Bu bir dilek listesi değil;
> her madde ya bir kullanıcı sorununu çözüyor ya da bir riski kapatıyor.
>
> ⚠️ **Güncel sıra artık [README](README.md)'deki tabloda** (04-13 numaralı
> dosyalar). Aşağıdaki liste, o dosyalar yazılmadan önceki ilk taslak — teknik
> bölümleri (§1-§8) hâlâ geçerli, ama önceliklendirme için README'ye bak.
>
> Sen hesap/karar bekleyen işler: [../new-phase/34-SENDEN-BEKLENENLER.md](../new-phase/34-SENDEN-BEKLENENLER.md)

---

## Sıra

| # | İş | Kim yapıyor | Neden bu sırada |
|---|----|-------------|-----------------|
| 0 | **Disk aç** | sen | 3,8 GB kaldı; container'lar düşüyor, her şeyi yavaşlatıyor |
| 0 | **Stripe'ta 2 fiyat** | sen | Ödeme kodu bitti; ürünün para kazanmasının önündeki tek engel |
| 1 | **Canlı birlikte düzenleme** | ben | Team'in vaadini tamamlayan tek büyük parça; altyapı zaten hazır |
| 2 | **Rol bazlı arayüz** | ben | Viewer bugün düzenleme araçlarını görüyor (sunucu reddediyor ama kullanıcıya yalan söylüyoruz) |
| 3 | **Bot'un kalanı** | ben (GitHub App gelince) | PR'da önizleme DB + `/namines` komutları |
| 4 | **Faturalamanın kenar durumları** | ben (Stripe gelince) | Başarısız ödeme, fatura geçmişi, plan değişimi |
| 5 | **Şemanın kalan detayları** | ben | View, RLS, `@ui`/`@tag`, şema adı |
| 6 | **Panelin kalanı** | ben | Dashboard, doğal dil sorgu, özelleştirme |

---

## 1. Canlı birlikte düzenleme 🔨 *sıradaki büyük iş*

**Sorun:** Team planı 3 koltuk, ortak workspace ve "kim ne değiştirdi" listesi
veriyor. Ama iki kişi aynı şemayı **aynı anda** açarsa birbirini görmüyor —
biri kaydettiğinde diğerinin çalışması sessizce eziliyor.

**Neden şimdi:** Ekip özelliğinin satılabilir olması için son parça bu. "Birlikte
çalışma" diye satılan bir şeyde iki kişinin birbirini ezmesi, özelliğin kendisini
yalanlar.

**Elimizde ne var:**
- SignalR kurulu ve `CanvasHub` çalışıyor (G6)
- Oda kimliği sunucu-otoriteli branch'e bağlı (G17) — rastgele oda yok
- `IPresenceStore` arayüzü var, bellek ve Redis implementasyonları hazır
- Organizasyon üyeliği ve roller var (G52)

**Eksik olan:**
1. **Ekip üyeliğinin odalara yetki kaynağı olması** — bugün oda branch'e bağlı
   ama "bu kullanıcı bu ekipte mi" kontrolü ekip modeline bağlanmadı
2. **İmleç ve seçim paylaşımı** — kim nereye bakıyor
3. **Çakışma çözümü** — asıl zor kısım (aşağıda)

**Zor karar — çakışma:** İki kişi aynı tabloyu aynı anda değiştirirse ne olacak?
Üç seçenek var ve **hiçbiri bedava değil**:

| Yaklaşım | Artı | Eksi |
|----------|------|------|
| Son yazan kazanır | Basit, bugün yazılır | Sessiz veri kaybı — bu kod tabanının kuralına aykırı |
| Alan bazlı kilit | Anlaşılır, veri kaybı yok | "Kilidi unutan" kullanıcı diğerini bloke eder |
| CRDT / OT | Gerçek eşzamanlı | Ciddi karmaşıklık, şema modelinin yeniden düşünülmesi |

**Öneri:** Alan bazlı kilit + otomatik bırakma (kullanıcı sekmeyi kapatınca veya
N saniye hareketsizlikte). Veri kaybı riski yok, karmaşıklık makul. CRDT ihtiyaç
kanıtlanmadan yazılmamalı.

> ⚠️ Bu, tasarımı **koddan önce** konuşulması gereken bir iş. Başlamadan önce
> yukarıdaki üç seçenekten hangisi olduğuna karar verilmeli.

**Redis gerekli mi:** Hayır. Tek sunucuda çalışır. İkinci sunucu gelirse
[02-REDIS-KARARI.md](02-REDIS-KARARI.md).

---

## 2. Rol bazlı arayüz 🔨 *küçük ama dürüstlük meselesi*

**Sorun:** `OrgRole` (Viewer / Editor / Admin / Owner) tanımlı ve **sunucu
uyguluyor** — yani güvenlik açığı yok. Ama Studio arayüzü rolü hiç dikkate
almıyor: Viewer da düzenleme araçlarını görüyor, tıklıyor, sunucu reddediyor.

**Neden yapılmalı:** Kullanıcıya yapamayacağı şeyi göstermek, onu hataya davet
etmek. Ayrıca "yetkiniz yok" hatası, tıklamadan önce görülmesi gereken bir şey.

**İş:** Canvas ve panel bileşenlerinin aktif rolü okuması, salt-okunur modda
düzenleme araçlarını gizlemesi. Yetki kaynağı yine sunucu — arayüz yalnızca
görüntüyü düzeltiyor, kontrolü değil.

---

## 3. Namines Bot'un kalanı ⏸ *GitHub App bekliyor*

Bot bugün PR'a yorum yazabiliyor ve status check basabiliyor (G43) — **ama
kimlik bilgisi olmadığı için hiç denemiyor.**

**GitHub App geldiğinde kalan:**
- PR açılınca otomatik **önizleme veritabanı** kurup "değişikliği burada dene"
  demesi
- `/namines plan`, `/namines approve` gibi komutların gerçekten çalışması
  (yazım hatası tahmini **yapılmayacak** — bilerek)

---

## 4. Faturalamanın kenar durumları ⏸ *Stripe bekliyor*

Mutlu yol yazıldı ve test edildi. Stripe'ta fiyatlar oluşunca kalan:
- Başarısız ödeme akışı (`past_due` → kullanıcıya ne gösteriliyor)
- Fatura geçmişi ekranı
- Plan yükseltme/düşürme kenar durumları (Team → Pro düşerken 3. üyeye ne olacak?)

> ⚠️ Sonuncusu gerçek bir tasarım sorusu: Team'den Pro'ya düşen bir hesapta
> fazladan üyeler ne olacak? Sessizce atmak veri/erişim kaybı; tutmak planı
> anlamsız kılar. Muhtemel cevap: erişim salt-okunura düşer, veri durur.

---

## 5. Şemanın kalan detayları

View, satır seviyesi güvenlik (RLS), `@ui`/`@tag` etiketleri, şema adı (`public`),
Migration IR, WASM derleyici.

En pahalı kısım (enum, `generated`, `collation`, dizi) G44–G45'te geçildi;
kalanlar daha küçük ve birbirinden bağımsız.

---

## 6. Panelin kalanı

Dashboard/grafik motoru, doğal dil sorgu, üretilen paneli özelleştirme, kolon
maskeleme ve satır filtresinin role bağlanması, **enum'ları arayüzden tanımlama**
(şu an yalnızca var olanlar seçilebiliyor).

> **`/query/nl` ve panelin doğal dil sorgusu aynı işin iki ucu.** İkisi de
> "üretilen SQL otomatik çalıştırılsın mı?" sorusuna cevap istiyor. Birlikte
> tasarlanmalı — yoksa aynı güvenlik kararı iki yerde iki farklı biçimde verilir.

---

## Bu klasörde ne kaydedilecek

Her iş bittiğinde buraya **ne yapıldığı değil, neyin öğrenildiği** yazılıyor:
hangi hata çıktı, hangi varsayım yanlıştı, hangi karar neden verildi.

Faz 1'in en değerli çıktısı kod değil, `CHECKLIST.md`'deki "bu G'de bulunan
gerçek hatalar" notlarıydı — aynı hataya iki kez düşmemeyi sağladılar.
