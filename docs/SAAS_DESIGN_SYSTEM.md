# SaaS Design System — Shopify-Inspired UI

**Dosya adı:** `SAAS_DESIGN_SYSTEM.md`  
**Tasarım referansı:** Shopify Admin / Shopify Polaris  
**Font ailesi:** Assistant  
**Tasarım türü:** Modern SaaS, dashboard, admin paneli ve e-ticaret yönetim arayüzü  
**Amaç:** Tüm sayfalarda tutarlı, profesyonel, modüler ve sürdürülebilir bir tasarım sistemi oluşturmak.

---

## 1. Tasarım Felsefesi

Arayüz, Shopify yönetim panelinin temel tasarım prensiplerini referans almalıdır:

- Sade ve işlevsel tasarım.
- İçeriği öne çıkaran açık renkli yüzeyler.
- Tutarlı kart, tablo ve form yapıları.
- Gereksiz gölge ve dekorasyondan kaçınma.
- Belirgin görsel hiyerarşi.
- Standartlaştırılmış boşluklar ve kenarlıklar.
- Yoğun verileri rahat okunabilir biçimde sunma.
- Masaüstü, tablet ve mobil uyumluluk.
- Tekrarlanabilir, yeniden kullanılabilir UI bileşenleri.

**Temel kural:** Her sayfa farklı bir tasarım dili kullanmamalıdır. Bütün arayüz aynı tasarım tokenlarını ve bileşen kurallarını kullanmalıdır.

Bu doküman Shopify'dan esinlenen bir tasarım standardıdır; resmi Shopify Polaris değerlerinin birebir kopyası değildir.

## 2. Genel Yerleşim (Layout)

Uygulama, temel olarak üç ana bölümden oluşmalıdır:

1. Global header / üst navigasyon.
2. Sidebar / yan navigasyon.
3. Main content / ana içerik alanı.

İçerik alanı, sayfanın amacına göre kartlar, tablolar, formlar, grafikler ve diğer modüler bileşenlerden oluşturulmalıdır.

### Desktop Layout

- Global header: yaklaşık `56–64px`.
- Sidebar: yaklaşık `240–260px`.
- Ana içerik alanı: kalan kullanılabilir genişlik.
- İçerik padding: `24–32px`.
- Ana bölüm aralığı: `24px`.
- Kartlar arası boşluk: `16–24px`.
- Sayfa genişliği: kullanılabilir alanı verimli kullanmalı; her sayfada gereksiz dar bir container oluşturulmamalıdır.

### Tablet Layout

- Sidebar daraltılabilir veya geçici navigasyona dönüştürülebilir.
- İçerik padding: `20–24px`.
- Kart grid yapıları ekran genişliğine göre yeniden düzenlenmelidir.
- Geniş tablolar için yatay kaydırma veya alternatif kompakt görünüm kullanılmalıdır.

### Mobile Layout

- Sidebar, drawer veya açılır navigasyon şeklinde çalışmalıdır.
- İçerik padding: `16px`.
- Kartlar çoğunlukla tek sütunda gösterilmelidir.
- Birincil aksiyonlar kolay erişilebilir olmalıdır.
- Tablolar gerektiğinde kart listelerine dönüştürülebilmelidir.
- İçerik yatay taşmaya neden olmamalıdır.

## 3. Renk Sistemi (Color Tokens)

Arayüzde nötr renkler temel alınmalıdır. Renkler dekorasyondan çok hiyerarşi, durum ve etkileşim belirtmek için kullanılmalıdır.

### Ana renkler

| Token | Önerilen değer | Kullanım |
|---|---|---|
| `background` | `#F6F6F7` | Genel uygulama arka planı |
| `surface` | `#FFFFFF` | Kartlar ve paneller |
| `surface-secondary` | `#F1F2F3` | İkincil yüzeyler |
| `text-primary` | `#202223` | Ana metin |
| `text-secondary` | `#616161` | İkincil metin |
| `text-muted` | `#8C9196` | Yardımcı metin |
| `border` | `#E1E3E5` | Standart kenarlıklar |
| `border-strong` | `#C9CCCF` | Daha belirgin sınırlar |
| `primary` | `#303030` | Birincil buton ve aksiyonlar |
| `primary-hover` | `#1F1F1F` | Hover durumu |
| `focus` | `#2C6ECB` | Klavye odak göstergesi |

Bu değerler projeye özel başlangıç tokenlarıdır; resmi Shopify renk paletinin birebir kopyası olarak değerlendirilmemelidir.

### Durum renkleri

| Durum | Arka plan | Metin | Kullanım |
|---|---|---|---|
| Success | `#E3F1DF` | `#176C36` | Başarılı işlemler |
| Warning | `#FFF4D6` | `#8A6116` | Dikkat gerektiren durumlar |
| Critical | `#FDE8E8` | `#B42318` | Hata ve başarısız işlemler |
| Info | `#E4F0FF` | `#174EA6` | Bilgilendirme |
| Neutral | `#F1F2F3` | `#42474C` | Varsayılan durum |

Durum renkleri sadece renk ile anlatılmamalıdır. Mümkün olduğunda ikon ve metin de kullanılmalıdır.

## 4. Spacing Sistemi

Bütün boşluklar standart bir ölçü sistemi üzerinden belirlenmelidir.

| Token | Değer | Kullanım |
|---|---:|---|
| `space-1` | 4px | İkon ve metin arası küçük boşluk |
| `space-2` | 8px | Etiket ve yardımcı metin aralığı |
| `space-3` | 12px | Kompakt bileşen içi boşluk |
| `space-4` | 16px | Standart bileşen boşluğu |
| `space-5` | 20px | Kart içi veya grup aralığı |
| `space-6` | 24px | Standart kart padding ve bölüm aralığı |
| `space-8` | 32px | Büyük bölüm aralığı |
| `space-10` | 40px | Bölümler arası geniş boşluk |
| `space-12` | 48px | Büyük sayfa bölümleri |
| `space-16` | 64px | Landing page bölümleri |

### Spacing kuralları

- İlişkili elemanlar birbirine yakın olmalıdır.
- Farklı içerik grupları daha geniş boşluklarla ayrılmalıdır.
- Kart içindeki elemanlar çoğunlukla `12–16px` aralık kullanmalıdır.
- Kartlar arasında `16–24px` boşluk tercih edilmelidir.
- Ana içerik bölümleri arasında `24–32px` boşluk kullanılmalıdır.
- Rastgele `13px`, `19px`, `27px` gibi değerler yerine tanımlı spacing ölçeği tercih edilmelidir.

## 5. Border Radius ve Kenarlıklar

### Border radius

| Token | Değer | Kullanım |
|---|---:|---|
| `radius-sm` | 4px | Küçük etiketler ve kompakt alanlar |
| `radius-md` | 8px | Butonlar, inputlar ve küçük kartlar |
| `radius-lg` | 12px | Ana kartlar ve paneller |
| `radius-xl` | 16px | Büyük tanıtım kartları |
| `radius-full` | 9999px | Pill badge ve yuvarlak avatarlar |

### Border kuralları

- Standart kartlarda `1px solid #E1E3E5` kullanılmalıdır.
- Input ve select alanlarında ince, nötr kenarlık tercih edilmelidir.
- Aynı yüzeyde hem yoğun gölge hem kalın kenarlık kullanılmamalıdır.
- Kenarlıklar içerik gruplarını ayırmak için kullanılmalı; her elemanı ayrı bir kutuya dönüştürmemelidir.
- Shopify tarzı yönetim panellerinde çoğu kart için `8–12px` radius yeterlidir.

## 6. Shadow Sistemi

Gölge kullanımı ölçülü olmalıdır.

| Token | Örnek değer | Kullanım |
|---|---|---|
| `shadow-none` | `none` | Standart yüzeyler |
| `shadow-sm` | `0 1px 2px rgba(0,0,0,0.05)` | Hafif yükselti |
| `shadow-md` | `0 4px 12px rgba(0,0,0,0.08)` | Dropdown ve popover |
| `shadow-lg` | `0 8px 24px rgba(0,0,0,0.12)` | Modal ve büyük overlay |

### Kurallar

- Normal kartlar çoğunlukla gölgesiz veya çok hafif gölgeli olmalıdır.
- Dropdown, popover ve modal gibi katmanlar arka plandan ayrılmalıdır.
- Hover sırasında ağır gölge animasyonları kullanılmamalıdır.
- Her kartın ayrı ayrı yükseliyormuş gibi görünmesinden kaçınılmalıdır.

## 7. Container ve Div Yapısı

HTML yapısı anlamsal ve modüler olmalıdır. Her element için gereksiz `div` oluşturulmamalıdır.

### Genel sayfa hiyerarşisi

```html
<div class="app-shell">
  <header class="app-header">
    <!-- Global navigation -->
  </header>

  <div class="app-body">
    <aside class="app-sidebar">
      <!-- Sidebar navigation -->
    </aside>

    <main class="app-main">
      <div class="page-container">
        <div class="page-header">
          <!-- Title and actions -->
        </div>

        <section class="page-content">
          <!-- Cards, tables and forms -->
        </section>
      </div>
    </main>
  </div>
</div>
```

Bu yapı kavramsal bir HTML örneğidir. Gerçek uygulamada erişilebilirlik, responsive davranış ve bileşen sınırları gözetilmelidir.

### Container kuralları

- `app-shell`: Uygulamanın ana kabuğu.
- `app-header`: Global navigasyon ve üst aksiyonlar.
- `app-body`: Sidebar ile ana içeriğin yerleşimi.
- `app-sidebar`: Sayfa navigasyonu.
- `app-main`: Ana içerik alanı.
- `page-container`: Sayfanın içerik genişliği ve padding kontrolü.
- `page-header`: Başlık, açıklama, breadcrumb ve aksiyonlar.
- `page-content`: Sayfanın gerçek içeriği.

Ana içerik içinde her şeyi tek bir büyük karta koymak yerine, yalnızca mantıksal olarak ilişkili içerikler gruplanmalıdır.

## 8. Header Tasarımı

Header, uygulama genelindeki navigasyon ve temel aksiyonlar için kullanılmalıdır.

### Önerilen özellikler

- Yükseklik: `56–64px`.
- Arka plan: `#FFFFFF` veya koyu bir global navigasyon yüzeyi.
- Alt kenarlık: `1px solid #E1E3E5`.
- Yatay padding: `16–24px`.
- Logo alanı: yaklaşık `140–180px`.
- İçerik: logo, arama, yardım, bildirim ve kullanıcı menüsü.
- İçerikler dikey olarak ortalanmalıdır.
- İkonlar aynı görsel boyut ve stroke kalınlığında olmalıdır.

### Header kuralları

- Gereksiz navigasyon bağlantıları eklenmemelidir.
- Kullanıcı menüsü ve bildirimler sağ tarafta gruplanabilir.
- Header ile sayfa içeriğinin hiyerarşisi net olmalıdır.
- Mobil görünümde ikincil aksiyonlar menü içine alınabilir.

## 9. Sidebar Tasarımı

Sidebar, ana uygulama bölümleri arasında hızlı geçiş sağlamalıdır.

### Önerilen özellikler

- Genişlik: `240–260px`.
- Daraltılmış genişlik: `64–80px`.
- Arka plan: beyaz veya ana uygulama yüzeyiyle uyumlu nötr ton.
- Menü satır yüksekliği: `36–44px`.
- Yatay padding: `12px`.
- İkon boyutu: `18–20px`.
- Menü metni: `14px`, ağırlık `500`.
- Menü öğeleri arası boşluk: `4–8px`.

### Aktif menü

Aktif öğe aşağıdaki özelliklere sahip olmalıdır:

- Belirgin bir arka plan veya seçili durum göstergesi.
- Daha güçlü metin kontrastı.
- Tutarlı ikon rengi.
- Klavye kullanıcıları için görünür focus davranışı.

Aktif menüde gereksiz parlak gradient veya yoğun gölge kullanılmamalıdır.

## 10. Page Header

Her yönetim sayfası standart bir başlık alanına sahip olmalıdır.

### Yapı

Sol tarafta:

- Breadcrumb (gerekiyorsa).
- Sayfa başlığı.
- Kısa açıklama veya bağlamsal bilgi.

Sağ tarafta:

- Birincil aksiyon.
- İkincil aksiyonlar.
- Gerektiğinde tarih aralığı veya filtre kontrolleri.

### Ölçüler

- Başlık: `24–30px`, ağırlık `600–700`.
- Açıklama: `14px`, ağırlık `400`.
- Breadcrumb: `12–13px`.
- Başlık ile açıklama arası: `4–8px`.
- Header ile ana içerik arası: `24px`.

Birincil aksiyon en görünür işlem olmalıdır. Birden fazla eşit önemde butonla görsel karmaşa oluşturulmamalıdır.

## 11. Card Sistemi

Kartlar, ilişkili verileri ve işlevleri bir araya getirmek için kullanılmalıdır.

### Standart kart

- Arka plan: `#FFFFFF`.
- Kenarlık: `1px solid #E1E3E5`.
- Border radius: `12px`.
- Padding: `20–24px`.
- Gölge: varsayılan olarak yok veya çok hafif.
- İçerik grupları arası boşluk: `12–16px`.

### Card yapısı

```html
<article class="card">
  <div class="card-header">
    <div class="card-heading">
      <h2 class="card-title">Card title</h2>
      <p class="card-description">Short description</p>
    </div>

    <div class="card-actions">
      <!-- Optional actions -->
    </div>
  </div>

  <div class="card-body">
    <!-- Main card content -->
  </div>

  <div class="card-footer">
    <!-- Optional secondary information -->
  </div>
</article>
```

### Card kuralları

- Her kartın tek bir ana amacı olmalıdır.
- Başlık, içerik ve footer görsel olarak ayrılmalıdır.
- Footer yalnızca ek bilgi veya aksiyon gerektiğinde kullanılmalıdır.
- Kartlar içinde kart kullanımı gereksiz yere tekrarlanmamalıdır.
- Kartlar sırf görsel çeşitlilik için farklı radius ve gölgeler kullanmamalıdır.

## 12. KPI ve İstatistik Kartları

Dashboard üzerindeki önemli metrikler hızlıca anlaşılabilmelidir.

### Kart yapısı

1. Metrik etiketi.
2. Büyük değer.
3. Önceki dönemle karşılaştırma.
4. Gerekirse küçük grafik veya trend göstergesi.

### Ölçüler

- Padding: `20–24px`.
- Etiket: `12–14px`, ağırlık `500`.
- Değer: `28–36px`, ağırlık `600–700`.
- Trend metni: `12–13px`.
- İçerik aralığı: `8–12px`.
- Kartlar arası boşluk: `16px`.

### Kurallar

- Değer görsel olarak en baskın öğe olmalıdır.
- Artış ve düşüş, yalnızca renkle değil işaret veya metinle de açıklanmalıdır.
- Ölçüm birimi net biçimde gösterilmelidir.
- Metriklerin karşılaştırıldığı zaman aralığı belirtilmelidir.
- Her KPI için grafik kullanılması zorunlu değildir.

## 13. Grid Sistemi

Kartlar ve dashboard bölümleri responsive grid yapısıyla yerleştirilmelidir.

### Önerilen kolon sayıları

| Ekran | KPI kartları | Genel kartlar |
|---|---:|---:|
| Geniş desktop | 4–6 | 2–3 |
| Standart desktop | 3–4 | 2 |
| Tablet | 2 | 1–2 |
| Mobil | 1–2 | 1 |

### Grid kuralları

- Kartlar aynı satırda tutarlı genişlikte olmalıdır.
- KPI kartları için `16px` gap iyi bir başlangıçtır.
- Daha büyük içerik kartlarında `20–24px` gap kullanılabilir.
- Sabit genişlik yerine mümkün olduğunda esnek kolonlar kullanılmalıdır.
- Birden fazla sütun için içerik önceliği gözetilmelidir.
- Mobilde okunabilirlik, kolon sayısından daha önemlidir.

## 14. Button Sistemi

Butonlar görsel önem sırasına göre sınıflandırılmalıdır.

### Primary Button

Ana işlemi gerçekleştirmek için kullanılır.

- Arka plan: koyu nötr veya marka rengi.
- Metin: beyaz veya yeterli kontrasta sahip renk.
- Font: `14px / 600`.
- Yükseklik: `36–40px`.
- Yatay padding: `12–16px`.
- Radius: `8px`.

### Secondary Button

İkincil işlemler için kullanılır.

- Arka plan: beyaz veya nötr açık zemin.
- Kenarlık: `1px solid #C9CCCF`.
- Metin: ana metin rengi.
- Font: `14px / 600`.
- Yükseklik: `36–40px`.

### Tertiary / Plain Button

Düşük öncelikli işlemler için kullanılır.

- Belirgin arka plan yok.
- Kenarlık yok.
- Metin ve ikon yeterli kontrasta sahip olmalıdır.
- Hover sırasında hafif yüzey rengi kullanılabilir.

### Destructive Button

Silme veya geri alınması zor işlemlerde kullanılır.

- Kritik işlem olduğu açıkça anlaşılmalıdır.
- Renk ve metin birlikte kullanılmalıdır.
- Gerekli durumlarda onay adımı eklenmelidir.

### Button durumları

- Default
- Hover
- Active
- Focus-visible
- Disabled
- Loading

Butonun tüm durumları tasarım sistemi içinde tanımlanmalıdır. Loading sırasında yinelenen tıklamalar engellenmelidir.

## 15. Input ve Form Sistemi

Form elemanları sade, tutarlı ve erişilebilir olmalıdır.

### Input

- Yükseklik: `36–40px`.
- Font boyutu: `14px`.
- Yatay padding: `12px`.
- Border: `1px solid #C9CCCF`.
- Radius: `8px`.
- Arka plan: yüzey rengi.

### Label

- Font: `13–14px`.
- Ağırlık: `500–600`.
- Input ile arası: `6–8px`.

### Helper text

- Font: `12–13px`.
- Ağırlık: `400`.
- Input ile arası: `4–6px`.

### Error message

- Font: `12–13px`.
- Kritik durum rengi.
- Hatanın nasıl düzeltileceğini mümkün olduğunda açıklamalıdır.

### Form kuralları

- Her input'un açık bir etiketi olmalıdır.
- Placeholder, label yerine kullanılmamalıdır.
- Zorunlu alanlar belirtilmelidir.
- Hata mesajları ilgili alanla ilişkilendirilmelidir.
- Başarılı ve başarısız validasyon durumları tutarlı olmalıdır.
- Formlar mantıksal gruplara ayrılmalıdır.
- Uzun formlarda bölüm başlıkları kullanılmalıdır.

## 16. Table Sistemi

Veri yoğun SaaS uygulamalarında tablolar temel bileşenlerden biridir.

### Table yapısı

- Table container.
- Table toolbar.
- Search ve filtre alanları.
- Header row.
- Data rows.
- Pagination veya sonuç sayısı.

### Ölçüler

- Tablo başlığı: `12–13px`, ağırlık `600`.
- Hücre metni: `14px`, ağırlık `400`.
- Satır yüksekliği: `44–52px`.
- Hücre yatay padding: `12–16px`.
- Hücre dikey padding: `12px`.
- Border: ince ve nötr.
- Header arka planı: yüzey veya çok hafif nötr yüzey.

### Table kuralları

- Metinsel alanlar sola hizalanmalıdır.
- Sayısal değerler tutarlı hizalanmalıdır.
- Para birimleri ve yüzdeler standart biçimde gösterilmelidir.
- Durumlar badge ile belirtilebilir.
- Satır hover efekti hafif olmalıdır.
- Satır aksiyonları mümkün olduğunca tutarlı konumlandırılmalıdır.
- Çok fazla kolon mobilde yatay kaydırma veya alternatif görünüm gerektirebilir.
- Boş veri durumunda açıklayıcı empty state gösterilmelidir.

## 17. Badge ve Status Sistemi

Badge, kısa durum veya kategori bilgisini belirtmek için kullanılır.

### Ölçüler

- Font: `12px`.
- Font ağırlığı: `500–600`.
- Yatay padding: `8px`.
- Dikey padding: `3–4px`.
- Radius: `9999px` veya kompakt yuvarlatılmış köşeler.

### Örnek durumlar

- Active
- Pending
- Draft
- Completed
- Failed
- Archived

Durum isimleri ürünün iş mantığına göre belirlenmelidir. Aynı durum bütün ekranlarda aynı renk ve isimle gösterilmelidir.

Badge, büyük açıklamaları veya uzun metinleri taşımak için kullanılmamalıdır.

## 18. Tabs Sistemi

Tabs, aynı bağlam içindeki ilişkili içerikler arasında geçiş sağlar.

### Kurallar

- Metin boyutu: `13–14px`.
- Font ağırlığı: `500–600`.
- Sekme yüksekliği: yaklaşık `40px`.
- Sekmeler arası yatay boşluk: `16–24px`.
- Aktif sekme açıkça ayırt edilmelidir.
- Aktif durum için alt çizgi veya hafif yüzey rengi kullanılabilir.
- Sekmeler mobilde taşmamalıdır.
- Sekmeler sayfalar arasında bağımsız navigasyon gerekiyorsa uygun bağlantı yapısıyla uygulanmalıdır.

## 19. Dropdown, Popover ve Tooltip

### Dropdown

- Yüzey arka planı.
- İnce kenarlık.
- Radius: `8–12px`.
- Gerekli olduğunda `shadow-md`.
- Menü öğeleri: `36–44px` yükseklik.
- Menü öğeleri arasında tutarlı boşluk.

### Popover

- İlgili kontrolle görsel bağlantı kurmalıdır.
- İç padding: `12–16px`.
- İçerik boyutuna göre genişlemelidir.
- Ekran kenarlarını aşmamalıdır.

### Tooltip

- Font: `12–13px`.
- Kompakt padding.
- Yeterli kontrast.
- Uzun veya kritik bilgilerin tek sunum yöntemi olmamalıdır.

Overlay bileşenlerinde klavye odağı, Escape ile kapatma ve doğru katman sırası gözetilmelidir.

## 20. Modal ve Drawer

Modal, kullanıcının mevcut akışını geçici olarak durduran önemli işlemler için kullanılmalıdır.

### Modal ölçüleri

- Küçük modal: `360–420px`.
- Orta modal: `480–600px`.
- Büyük modal: `640–800px`.
- İç padding: `24px`.
- Başlık: `20–24px / 600`.
- Gövde: `14px / 400`.
- Footer aksiyonları: tutarlı buton aralıkları.

Boyutlar içerik ve ekran genişliğine göre uyarlanmalıdır.

### Kurallar

- Gereksiz modal kullanımından kaçınılmalıdır.
- Basit bilgi göstermek için modal açılmamalıdır.
- Kritik işlemlerde sonuç açıkça belirtilmelidir.
- Modal kapatıldığında kullanıcı bağlamı korunmalıdır.
- Mobilde modal tam ekran veya uygun bir drawer görünümüne dönüşebilir.

## 21. Empty State

Veri bulunmadığında boş ekran yerine yönlendirici bir görünüm kullanılmalıdır.

### İçerik

1. İsteğe bağlı ikon veya illüstrasyon.
2. Başlık.
3. Kısa açıklama.
4. Uygunsa birincil aksiyon.

### Ölçüler

- Başlık: `18–20px / 600`.
- Açıklama: `14px / 400`.
- Aksiyon: standart buton sistemi.
- Dikey ve yatay hizalama: içeriğin bağlamına uygun.

Empty state, kullanıcının neden veri görmediğini ve sonraki adımın ne olduğunu açıklamalıdır.

## 22. Loading, Skeleton ve Feedback

### Loading

- İşlem sürerken kullanıcının durumdan haberdar olması sağlanmalıdır.
- Uzun işlemlerde ilerleme bilgisi gerekiyorsa sunulmalıdır.
- Buton loading durumunda tekrar gönderim engellenmelidir.

### Skeleton

- İçeriğin beklenen düzenini yansıtmalıdır.
- Gerçek içerik yüklenince düzen mümkün olduğunca korunmalıdır.
- Gereksiz ve aşırı animasyon kullanılmamalıdır.

### Toast / Notification

- Başarılı, bilgi, uyarı ve hata mesajları tutarlı olmalıdır.
- Mesaj kısa ve açıklayıcı olmalıdır.
- Kritik hata yalnızca kısa süre görünen toast mesajına bırakılmamalıdır.
- Kullanıcıya gerektiğinde yeniden deneme veya düzeltme yolu sunulmalıdır.

## 23. Icon Sistemi

- Tek bir ikon kütüphanesi tercih edilmelidir (Lucide).
- İkonların çizgi kalınlığı tutarlı olmalıdır.
- Sidebar ikonları: `18–20px`.
- Buton ikonları: `16–18px`.
- Büyük durum ikonları: `20–24px`.
- İkon ile metin arasında `8px` boşluk kullanılabilir.
- Dekoratif olmayan ikonlar erişilebilir bir isim taşımalıdır.
- Yalnızca ikon içeren butonlarda erişilebilir etiket bulunmalıdır.

İkonlar gereksiz yere farklı renklerde kullanılmamalıdır. Renk, işlevi veya durumu açıklamak için tercih edilmelidir.

## 24. Görsel ve Grafik Kartları

Grafikler bir kart içine yerleştirildiğinde standart kart yapısıyla uyumlu olmalıdır.

### Grafik kartı

- Kart başlığı.
- İsteğe bağlı açıklama veya zaman aralığı.
- Grafik alanı.
- İsteğe bağlı legend.
- Gerekliyse alt bilgi.

### Kurallar

- Grafik başlığı ve ölçüm birimi açık olmalıdır.
- Renkler kategoriler arasında tutarlı olmalıdır.
- Tooltip ve legend okunabilir olmalıdır.
- Grafikler farklı ekranlarda taşmamalıdır.
- Verinin olmadığı durumda uygun empty state kullanılmalıdır.
- Renk dışında şekil, etiket veya metin desteği sağlanmalıdır.

## 25. Landing Page Tasarımı

Landing page, dashboard ile aynı tasarım tokenlarını kullanmalı; ancak daha geniş boşluklara ve daha güçlü başlık hiyerarşisine sahip olmalıdır.

### Hero

- Büyük başlık: `48–56px`.
- Açıklama: `16–18px`.
- Ana CTA: `40–48px` yükseklik.
- Başlık ve açıklama arası: `16–24px`.
- Hero ile sonraki bölüm arası: `48–64px` veya daha fazla.

### İçerik bölümleri

- Bölüm başlığı: `30–36px`.
- Açıklama: `14–18px`.
- Kart aralığı: `16–24px`.
- Bölümler arası boşluk: `48–80px`.

### Kurallar

- Hero alanında tek bir ana mesaj bulunmalıdır.
- Birincil CTA net olmalıdır.
- Çok sayıda gradient ve dekoratif efekt kullanılmamalıdır.
- Özellik kartları aynı hiyerarşiye sahip olmalıdır.
- Mobilde başlık boyutları ve kolon sayıları uyarlanmalıdır.

## 26. Erişilebilirlik

- Metin ve arka plan arasında yeterli kontrast sağlanmalıdır.
- Normal metinlerde WCAG AA kontrastı hedeflenmelidir.
- Klavye navigasyonu desteklenmelidir.
- Focus göstergeleri görünür olmalıdır.
- Form kontrollerinin erişilebilir etiketleri bulunmalıdır.
- Hata mesajları yalnızca renk ile anlatılmamalıdır.
- Buton ve bağlantıların amacı açık olmalıdır.
- Etkileşimli hedefler dokunmatik cihazlarda rahat kullanılmalıdır.
- Hareket azaltma tercihleri dikkate alınmalıdır.

## 27. Responsive Davranış Kuralları

### Desktop

- Sidebar görünür olmalıdır.
- Ana içerik kalan alanı kullanmalıdır.
- Geniş veri tabloları gösterilebilir.
- Dashboard grid yapıları 3–4 veya daha fazla kolona çıkabilir.

### Tablet

- Sidebar daraltılabilir.
- KPI kartları çoğunlukla 2 kolona düşmelidir.
- İçerik grupları gerektiğinde alt alta alınmalıdır.
- Tablo genişlikleri kontrol edilmelidir.

### Mobil

- Navigasyon kompakt hale getirilmelidir.
- Kartlar tek kolona geçmelidir.
- Formlar çoğunlukla tam genişlikte olmalıdır.
- Birincil aksiyon kolay bulunmalıdır.
- Tablo yerine kart listeleme tercih edilebilir.
- Modal ve dropdown ekran sınırlarını aşmamalıdır.

Responsive davranış yalnızca ekranı küçültmek değildir; içerik önceliğini ve etkileşim biçimini de uyarlamaktır.

## 28. Tasarım Tokenları — CSS Referansı

Aşağıdaki değişkenler sistemin temel görsel değerlerini merkezi olarak tanımlar.

```css
:root {
  /* Colors */
  --color-background: #F6F6F7;
  --color-surface: #FFFFFF;
  --color-surface-secondary: #F1F2F3;

  --color-text-primary: #202223;
  --color-text-secondary: #616161;
  --color-text-muted: #8C9196;

  --color-border: #E1E3E5;
  --color-border-strong: #C9CCCF;

  --color-primary: #303030;
  --color-primary-hover: #1F1F1F;
  --color-focus: #2C6ECB;

  /* Spacing */
  --space-1: 4px;
  --space-2: 8px;
  --space-3: 12px;
  --space-4: 16px;
  --space-5: 20px;
  --space-6: 24px;
  --space-8: 32px;
  --space-10: 40px;
  --space-12: 48px;
  --space-16: 64px;

  /* Radius */
  --radius-sm: 4px;
  --radius-md: 8px;
  --radius-lg: 12px;
  --radius-xl: 16px;
  --radius-full: 9999px;

  /* Borders */
  --border-width: 1px;
  --border-default: 1px solid var(--color-border);

  /* Shadows */
  --shadow-none: none;
  --shadow-sm: 0 1px 2px rgba(0, 0, 0, 0.05);
  --shadow-md: 0 4px 12px rgba(0, 0, 0, 0.08);
  --shadow-lg: 0 8px 24px rgba(0, 0, 0, 0.12);

  /* Layout */
  --header-height: 60px;
  --sidebar-width: 250px;
  --sidebar-collapsed-width: 72px;
  --page-padding: 24px;
  --card-padding: 24px;
  --content-gap: 24px;
}
```

Bu tokenlar başlangıç değerleridir. Gerçek projede tasarımın tamamı aynı değişkenleri kullanmalı; tekrar eden sabit değerler azaltılmalıdır.

## 29. Component Naming Standardı

Bileşen isimleri anlaşılır ve işlev odaklı olmalıdır.

Önerilen isimler:

- `AppShell`
- `AppHeader`
- `AppSidebar`
- `PageContainer`
- `PageHeader`
- `PageActions`
- `Card`
- `CardHeader`
- `CardBody`
- `CardFooter`
- `KpiCard`
- `DataTable`
- `TableToolbar`
- `TablePagination`
- `Button`
- `Input`
- `Select`
- `Badge`
- `Tabs`
- `Dropdown`
- `Popover`
- `Tooltip`
- `Modal`
- `Drawer`
- `EmptyState`
- `Skeleton`
- `Toast`

### Bileşen kuralları

- Aynı görsel ve davranışsal işleve sahip elemanlar ortak bileşen kullanmalıdır.
- Sayfaya özel mantık, genel tasarım bileşenlerinden ayrılmalıdır.
- Bileşenler gerekli durumlarda boyut ve varyant kabul edebilmelidir.
- Aynı bileşenin kopyalanmış farklı sürümleri oluşturulmamalıdır.
- Bileşenler erişilebilirlik ve responsive kurallarıyla birlikte geliştirilmelidir.

## 30. Uygulanmaması Gereken Tasarım Hataları

Aşağıdaki hatalardan kaçınılmalıdır:

- Her karta farklı radius vermek.
- Gereksiz gradient kullanmak.
- Her kartın altına yoğun gölge eklemek.
- Bütün içerikleri kart içine yerleştirmek.
- Aynı sayfada çok fazla vurgu rengi kullanmak.
- Gereksiz büyük başlıklar ve boşluklarla dashboard verimliliğini düşürmek.
- Her aksiyonu primary button yapmak.
- Placeholder metnini input etiketi yerine kullanmak.
- Küçük metinleri okunamayacak seviyeye indirmek.
- Farklı sayfalarda farklı tablo satırı yükseklikleri kullanmak.
- Hover animasyonlarını gereğinden fazla büyütmek.
- Mobil cihazlarda masaüstü layout'unu aynen korumak.
- Hata ve başarı durumlarını yalnızca renk ile göstermek.
- Her bileşen için yeniden CSS değerleri üretmek.
- Erişilebilirlik ve klavye navigasyonunu göz ardı etmek.

## 31. Nihai Tasarım Standartları

| Özellik | Standart |
|---|---|
| Font | Assistant |
| Genel arka plan | `#F6F6F7` |
| Kart arka planı | `#FFFFFF` |
| Ana metin | `#202223` |
| İkincil metin | `#616161` |
| Standart border | `1px solid #E1E3E5` |
| Kart radius | `12px` |
| Input radius | `8px` |
| Button radius | `8px` |
| Kart padding | `20–24px` |
| İçerik padding | `24px` |
| Kartlar arası gap | `16–24px` |
| Ana bölüm gap | `24–32px` |
| Header yüksekliği | `56–64px` |
| Sidebar genişliği | `240–260px` |
| Standart buton yüksekliği | `36–40px` |
| Standart input yüksekliği | `36–40px` |
| Tablo satırı yüksekliği | `44–52px` |
| Standart metin | `14px` |
| Kart başlığı | `16–18px` |
| Dashboard başlığı | `24–30px` |

---

## 32. Geliştirme İçin Son Kontrol Listesi

- [x] Tüm sayfalar aynı renk tokenlarını kullanıyor.
- [x] Tüm kartlar ortak radius, border ve padding kurallarına uyuyor.
- [x] Header ve sidebar bütün sayfalarda tutarlı.
- [x] Başlık hiyerarşisi ve tipografi sistemi korunuyor.
- [x] Butonların tüm durumları tanımlanmış.
- [x] Input, select ve form hata durumları standart.
- [x] Tablolar ortak satır yüksekliği ve hücre boşlukları kullanıyor.
- [x] KPI kartları aynı görsel hiyerarşiye sahip.
- [x] Mobil ve tablet davranışları test edilmiş.
- [x] Klavye navigasyonu ve focus göstergeleri çalışıyor.
- [x] Boş veri, loading ve hata durumları tasarlanmış.
- [x] Renk kontrastları kontrol edilmiş.
- [x] Tekrarlanan UI elemanları ortak bileşenlerden oluşturulmuş.
- [x] Gereksiz gölge, gradient ve dekoratif efektler kaldırılmış.
- [x] Sayfalar arasında tutarsız spacing değerleri temizlenmiş.

**Sonuç:** Bu doküman, Assistant fontuyla birlikte kullanılacak Shopify esintili SaaS tasarım sisteminin referansıdır. Yeni sayfalar geliştirilirken yerleşim, kartlar, tablolar, formlar, navigasyon ve etkileşimli bileşenler bu standartlara uygun oluşturulmalıdır.
