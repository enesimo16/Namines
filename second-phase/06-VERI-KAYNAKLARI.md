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

**Bu yöntem tamamen kaldırılıyor.** Yerine gelen şey aşağıdaki kademeli
zincir.

**Ayrıca netleşen sınır:** bir sitenin **gerçek veritabanını** dışarıdan okumak
mümkün değil — extension da okuyamaz, o yalnızca DOM ve ağ trafiğini görür.
Bu yüzden hedef "sitenin DB'sini kopyala" değil, **"sitenin dışa açtığı veri
modelini çıkarım yoluyla tahmin et"** olarak yeniden tanımlandı.

## Kademeli çıkarım zinciri

Tek bir yönteme güvenmek yerine, güçlüden zayıfa düşen bir zincir. Her
kaynak başarısız olursa bir alttakine düşülür; hiçbiri çalışmazsa **boş
dönülür, uydurulmaz.**

| Sıra | Kaynak | Ne verir | Güvenilirlik | Ne kadar yaygın |
|------|--------|----------|---------------|-------------------|
| 1 | **GraphQL introspection** (`__schema`) | Tam tip grafiği | 🟢🟢 Yüksek | Nadir — çoğu üretim sitesi kapatır |
| 2 | **OpenAPI/Swagger dokümanı** | Alan tipleri, zorunluluklar | 🟢🟢 Yüksek | Az sayıda site yayınlar |
| 3 | **Gözlemlenen JSON trafiğinin şekli** | Tekrar eden alanlardan (`id`, `user_id`, `created_at`) varlık/ilişki çıkarımı | 🟡 Orta | **Asıl geniş kapsayan katman — çoğu modern sitede JSON API vardır** |
| 4 | **DOM form/tablo alanları** | Alan adları, tipleri, zorunlu işaretleri | 🟡 Zayıf | JS'siz eski sitelerde bile çalışır |
| — | *(hiçbiri yoksa)* | — | — | **"Veri modeli çıkarılamadı" — boş dönülür** |

**1 ve 2 bir garanti değil, bir bonus.** Asıl kapsamı sağlayan katman 3 —
GraphQL/OpenAPI olmayan sitelerde bile JSON API neredeyse her zaman vardır,
sadece dokümante edilmemiştir.

**Çıkarılan her şema "tahmin" etiketiyle gelmeli.** Üretilen şema "sitenin
veritabanı" değil, "API'sinden çıkarılan veri modeli". Gerçek DB
denormalize olabilir, iç tabloları olabilir. Bunu gizlemek, kaldırdığımız
yalanın yerine yenisini koymak olur.

### 3. numara nasıl çalışır — JSON şekil çıkarımı

Bu katman formal bir API tanımına ihtiyaç duymuyor; extension'ın zaten
gördüğü `fetch`/XHR yanıtlarından çalışıyor:

1. **Topla** — sayfa gezilirken dönen her JSON yanıtı (yalnızca gövde şekli,
   değerler değil) kaydedilir.
2. **Kümele** — aynı alan adı+tipi setine sahip yanıtlar aynı "varlık" sayılır
   (`{id, email, name}` dönen her yanıt → `User` adayı).
3. **İlişki çıkar** — `xxx_id` / `xxxId` deseni taşıyan alanlar, adı eşleşen
   başka bir varlığa işaret ediyorsa yabancı anahtar adayı sayılır
   (`order.user_id` → `User` varlığına bağ).
4. **Güven puanı ver** — bir şekil kaç farklı uç noktada, kaç kez görüldüyse
   güveni o kadar yüksek. Tek yanıtta görülen bir alan "belirsiz" işaretlenir.
5. **Kullanıcıya göster, dayatma** — sonuç bir taslak; kullanıcı varlığı
   kabul eder, yeniden adlandırır ya da reddeder. Otomatik onay yok.

Değerler (gerçek e-posta, isim, fiyat) **hiçbir aşamada saklanmaz** — yalnızca
alan adı ve tip. Bu hem gizlilik hem kapsam açısından doğru: şema çıkarımı
için veriye değil, veri **şekline** ihtiyaç var.

## Toplama biçimi — pasif varsayılan, aktif yalnızca izinle

| Yaklaşım | Ne yapıyor | Durum |
|----------|-----------|-------|
| **Pasif, kısa pencere** | Kullanıcı geziniyor, extension yalnızca gözlemliyor | 🟢 Varsayılan |
| **Pasif, uzun pencere** (ör. 10 dk) | Aynısı, yalnızca süre uzun | 🟢 Hâlâ pasif, sorun yok |
| **Kullanıcı onaylı aktif tarama** | "Bu sitede daha fazla veri olabilir, taramamı ister misin?" — kullanıcı evet derse `robots.txt`'e uyarak **kapsamlı** tarama | 🟢 Rıza varsa sınırlama yok |
| Gizli / algılama-atlatmalı tarama | İstekleri yapay yavaşlatma, insan taklidi, vb. | 🔴 **Tasarlanmayacak** |

**Rıza varsa çekinmeye gerek yok.** Kullanıcı kendi ziyaret ettiği bir siteyi
taramamıza izin veriyorsa, "birkaç istekle" sınırlamanın bir anlamı yok —
kapsamlı tarayabiliriz. **Doğal güvenlik supabı zaten var:** site bunu bot
davranışı sayıp yavaşlatır/durdurursa (429, geçici IP engeli), extension bunu
**olduğu gibi kabul eder ve durur** — "veri modeli çıkarılamadı" döner.
Bu bir arıza değil, beklenen bir sonuç: rıza karşı tarafın onayını
garanti etmiyor, yalnızca bizim tarafımızda niyetin dürüst olduğunu gösteriyor.
Site kapıyı kapatırsa açmaya çalışmayız.

**Neden gizli tarama hâlâ çizgi dışı:** rızayla bile, algılamayı **atlatmaya**
çalışmak ayrı bir şey. Rıza "kullanıcı bu taramayı istiyor" demek; algılama
atlatma "site bunu istemiyor ama fark etmesin" demek. İkincisi rızanın
kapsamına girmiyor — kullanıcı izin verebileceği şey yalnızca **kendi**
tarafındaki eylem, sitenin savunmasını atlatma yetkisini veremez.

## Localhost — sunucudan asla, yalnızca yerel ajandan

Sunucu kullanıcının iç ağına **hiçbir zaman** ulaşmamalı. `SsrfGuard`
loopback ve özel IP aralıklarını bilerek kapatıyor — bu bir eksik değil,
güvenlik kararı. Yerel veritabanına erişim yalnızca **kullanıcının kendi
makinesinde çalışan CLI** üzerinden olur (`namines connect`), zaten var.

## ⚠️ Dikkat

- **Çıkarım olduğu her ekranda söylenmeli**, tek seferlik bir uyarı olarak değil.
- **Uzunluk sınırı zorunlu** — hangi kaynaktan gelirse gelsin.
- Extension yalnızca **kullanıcının zaten yaptığı** istekleri gözlemlemeli
  (pasif mod) ya da **açıkça onay verdiği** ek istekleri atmalı (aktif mod).
- Kimlik doğrulama başlıkları, çerezler, token'lar **asla** Namines'e
  gönderilmez — extension şekil çıkarır, veri ya da kimlik değil.

## 🔴 Yapılmayacak

- **Gizli/algılama-atlatmalı tarama.** Ne kadar yavaş olursa olsun.
- **Rızasız aktif tarama.** Pasif gözlem her zaman varsayılan; aktife geçiş
  her seferinde kullanıcıya sorulur, arka planda otomatik açılmaz.
- **Site engellediğinde ısrar etmek.** 429/403/IP engeli geldiğinde yeniden
  deneme, farklı IP/User-Agent deneme yok — durulur, sonuç boş döner.
- "Sitenin veritabanını çıkardık" ifadesi — hiçbir yerde. Çıkarılan şey
  bir modeldir; öyle adlandırılmalı.
- Hiçbir kaynak çalışmadığında **sayfa metnine düşmek.** Boş sonuç, yanlış
  sonuçtan iyidir.
