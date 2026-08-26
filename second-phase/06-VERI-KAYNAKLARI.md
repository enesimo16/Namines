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

## Toplama biçimi — pasif varsayılan, aktif yalnızca izinle

| Yaklaşım | Ne yapıyor | Durum |
|----------|-----------|-------|
| **Pasif, kısa pencere** | Kullanıcı geziniyor, extension yalnızca gözlemliyor | 🟢 Varsayılan |
| **Pasif, uzun pencere** (ör. 10 dk) | Aynısı, yalnızca süre uzun | 🟢 Hâlâ pasif, sorun yok |
| **Kullanıcı onaylı aktif tarama** | "Bu sitede daha fazla veri olabilir, taramamı ister misin?" — kullanıcı evet derse `robots.txt`'e uyarak birkaç ek istek | 🟡 Yalnızca açık rızayla |
| Gizli / algılama-atlatmalı tarama | İstekleri yapay yavaşlatma, insan taklidi, vb. | 🔴 **Tasarlanmayacak** |

**Neden gizli tarama çizgi dışı:** sorun hız değil, izin. Bir sitenin bot
korumasını bilerek atlatmak, "ben insanım" yalanı söylemek demek — bu
hızdan bağımsız olarak sorunlu. Yavaşlatmak sunucu yükünü azaltır, izin
almanızı sağlamaz. Doğru çözüm gizlilik değil **rıza**: kullanıcıya sorup
onay almak.

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
- "Sitenin veritabanını çıkardık" ifadesi — hiçbir yerde. Çıkarılan şey
  bir modeldir; öyle adlandırılmalı.
- Hiçbir kaynak çalışmadığında **sayfa metnine düşmek.** Boş sonuç, yanlış
  sonuçtan iyidir.
