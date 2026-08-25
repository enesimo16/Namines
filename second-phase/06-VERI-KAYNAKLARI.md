# 06 — Veri Kaynakları: URL, API, Extension, Localhost

> **Sıra: 3.** Bugün burada **yalan söyleyen** bir özellik var; önce o
> düzeltilmeli.

---

## Bugünkü durum — çalışmıyor

`ReferenceUrl` özelliği şunu yapıyor: sayfanın HTML'ini indiriyor, **görünen
metni** alıyor, boşlukları sıkıştırıyor ve **tamamını** prompt'a ekliyor.

Üç ayrı kırık nokta:

1. **Uzunluk sınırı yok** — büyük bir sayfa on binlerce token'ı prompt'a basıyor
2. Bir sitenin görünen metni, veritabanı şeması hakkında neredeyse hiçbir şey
   söylemiyor. Pazarlama metninden tablo çıkarılıyor
3. Modern siteler JS ile render oluyor → sunucu boş bir kabuk indiriyor

**Bir sitenin veritabanını dışarıdan okumak mümkün değil.** Extension da
okuyamaz — DOM ve ağ trafiği görür, veritabanını görmez. Bu yüzden "sitenin
DB'sini birebir çıkar" vaadi **hiçbir teknikle** karşılanamaz.

## Gerçekten mümkün olan: veri modelini ÇIKARIM ile bulmak

Extension'ın göreceği şeyler, güçlüden zayıfa:

| Kaynak | Ne verir | Güç |
|--------|----------|-----|
| **GraphQL introspection** (`__schema`) | Tam tip grafiği: tipler, alanlar, ilişkiler | 🟢🟢 Neredeyse şemanın kendisi |
| **OpenAPI / Swagger** (`/openapi.json`, `/swagger.json`) | Şemalar, alan tipleri, zorunluluklar | 🟢🟢 Çok güçlü |
| **Ağ trafiği** (kullanıcı gezinirken) | Uç noktalar (`/api/orders`), JSON gövde şekilleri, `user_id` gibi ilişki ipuçları | 🟢 Gerçek alan adları |
| **Formlar ve tablolar** (DOM) | Alan adları, tipleri, zorunlu alanlar | 🟡 Zayıf ama gerçek |
| Sayfa metni | — | 🔴 Bugünkü yol. Değersiz |

Yani doğru çerçeve: **"sitenin DB'sini kopyala" değil, "sitenin API'sinden
veri modelini çıkar."** Bu hem mümkün hem dürüst hem daha faydalı — çıkan
şey pazarlama metni değil, gerçek alan adları.

## Nasıl (aşamalı)

1. **Adım 1 — sunucu tarafı:** `ReferenceUrl` yerine `ApiSpecUrl`.
   OpenAPI/GraphQL adresi verilir, şema oradan çıkarılır. Extension gerekmez.
2. **Adım 2 — extension:** kullanıcı siteyi gezerken **kendi tarayıcısının**
   yaptığı istekleri gözlemler, JSON şekillerinden varlık çıkarır, "Namines'e
   gönder" der.
3. **Adım 3 — localhost:** sunucu kullanıcının iç ağına ulaşamaz ve
   ulaşmamalı (bkz. aşağıda). Yerel veritabanı için **CLI** kullanılır:
   `namines connect` makinede çalışır, şemayı okur, gönderir. CLI zaten var.

## ⚠️ Dikkat

- **Çıkarım olduğu SÖYLENMELİ.** Üretilen şema "sitenin veritabanı" değil,
  "API'sinden çıkarılan veri modeli". Gerçek DB denormalize olabilir, iç
  tabloları vardır. Bunu gizlemek, bugünkü yalanın yerine yenisini koymak olur.
- **Uzunluk sınırı zorunlu.** Hangi kaynaktan gelirse gelsin, prompt'a eklenen
  içerik kırpılmalı — bugünkü sınırsız hâl kotayı tek istekte yakabilir.
- **Localhost'u sunucuya açma.** `SsrfGuard` loopback ve özel IP aralıklarını
  bilerek kapatıyor; bu bir eksik değil, güvenlik kararı. Sunucunun kullanıcının
  iç ağına uzanması ciddi bir açıktır. Yerel erişim **yalnızca kullanıcının
  makinesinde çalışan ajandan** geçer.
- Extension yalnızca **kullanıcının zaten yaptığı** istekleri gözlemlemeli.

## 🔴 Yapılmayacak

- **Uç nokta taraması / crawl.** Extension bir sitenin API'sini kendiliğinden
  taramamalı — bu, kullanıcı adına izinsiz tarama olur ve hem etik hem yasal
  sorundur. Yalnızca kullanıcının tarayıcısının zaten yaptığı istekler.
- Kimlik doğrulama başlıklarını, çerezleri ya da token'ları Namines'e göndermek.
  Extension **şekil** çıkarır, veri ya da kimlik değil.
- "Sitenin veritabanını çıkardık" ifadesi — hiçbir yerde. Çıkarılan şey bir
  modeldir; öyle adlandırılmalı.
