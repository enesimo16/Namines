# Ground'un konumu — ürün kararı

**Son güncelleme:** 12.09.2026 · Kaynak bulgu: `RW-02`
(bkz. [19-remove-improve-features.md](audit/19-remove-improve-features.md)) ·
Backlog: `B-26`

> **Bu bir öneridir, onaylanmış bir karar değil.** Ürün sahibinin onayına
> kadar geçerli konum §2'deki "bugünkü durum"dur. Karar verildiğinde bu
> belgenin başına onay tarihi yazılmalı.

---

## 1. Karar önerisi: Ground bir ALTYAPI ürünü değil, bir BAŞLANGIÇ ZEMİNİ

| Soru | Öneri |
|---|---|
| Ground, Supabase/Neon'a rakip bir barındırma ürünü mü? | **Hayır.** Ve bu yarışa girmek yanlış |
| O zaman ne? | Şemayı **denemek** için sürtünmesiz bir zemin: tek tıkla geçici bir veritabanı |
| Üretim yükü taşıyacak mı? | **Hayır.** Üretim için kullanıcının kendi platformu (Supabase, Neon, kendi PostgreSQL'i) |
| Namines'in asıl ürünü nerede? | O platformların **üstünde**: şema tasarımı, değişiklik onayı, denetim kaydı, yedek, erişim izinleri |

**Tek cümle:** *"Veritabanınızı biz barındırmaya çalışmıyoruz —
barındırdığınız yerin üstüne yönetişim koyuyoruz."*

---

## 2. Bugünkü durum

| Öğe | Durum |
|---|---|
| Kendi barındırdığımız PostgreSQL (`LocalPostgresProvider`) | ✅ Var, Free planda 1 veritabanı |
| Neon sağlayıcısı | ✅ Var, **canlı doğrulandı** (09.09.2026) |
| Supabase sağlayıcısı | ✅ Kod var (B-45), **canlı doğrulanmadı** — jeton yok, `IsLiveVerified: false` |
| Boyut uyarısı | ✅ Yalnızca **uyarı** — kısıtlama, silme, yazma erişimi kaldırma YOK |
| Sağlayıcı seçimi | ✅ Koleksiyon olarak kayıtlı; kullanıcı seçiyor |

Yani altyapı **çoklu sağlayıcı** olarak zaten kurulmuş. Eksik olan yazılı
konumlandırma: hangi sağlayıcının ne için önerildiği.

---

## 3. Neden altyapı yarışına girilmemeli — ölçülmüş gerekçe

`16-competitor-analysis.md`'nin ölçtüğü şey:

> Supabase'in Free planı bile (500 MB, 2 proje) Ground'un v1 limitlerinin
> **üstünde** ve arkasında yıllarca altyapı yatırımı var.

Buna üç şey eklenir:

1. **Maliyet yapısı bize karşı.** Barındırma, kullanıcı başına sabit gider
   demek; yönetişim katmanı ise yazılım marjıyla çalışır. Free planda
   barındırma vermek, en ucuz müşteriye en pahalı hizmeti vermek olur.
2. **Sorumluluk asimetrisi.** Bir veritabanı barındırmak, gece 3'te veri
   kaybından sorumlu olmak demek. `IDatabaseProvider.ResponsibilityNote`
   alanının var olma sebebi tam olarak bu ayrımı kullanıcıya söylemek.
3. **Rakiplerin kendisi bu platformlar.** Onların üstünde çalışan bir
   yönetişim aracı, onlarla yarışan bir barındırıcıdan **daha savunulabilir**
   bir konum — F-10'un "stratejik değeri en yüksek madde" demesinin sebebi.

---

## 4. Kararın gerektirdiği işler

| # | İş | Efor | Neden |
|---|---|---|---|
| 1 | Arayüzde sağlayıcı seçimine "ne için uygun" etiketi (Ground: *deneme/geliştirme*, Neon/Supabase: *üretim*) | S | Kullanıcı yanlış zemine üretim yükü koymasın |
| 2 | Supabase sağlayıcısını canlı doğrula (jeton gerekiyor) ve `IsLiveVerified` bayrağını bilinçli çevir | S | Bugün kod var, kanıt yok |
| 3 | Free plan metninde Ground'un **kalıcı üretim** için önerilmediğini yaz | XS | Beklenti yönetimi; sonradan "veri kaybettim" tartışmasının önünü keser |
| 4 | Pazarlama sayfasında konumu yaz | XS | Karar yazılı değilse satış ekibi onu kendi cümlesiyle uydurur |

**Yapılmayacak:** Ground'a üretim SLA'sı, yedek garantisi, ölçeklendirme
sözü. Bu karar değişirse ayrı bir ürün kararı olarak yeniden ele alınır.

---

## 5. Bu belge neyi kanıtlamıyor

- **Pazar araştırması bu oturumda tamamlanmadı.** `16-competitor-analysis.md`
  kendi başlığında bazı satırları "⚠️ doğrulanmadı" olarak işaretliyor;
  buradaki gerekçe o raporun doğrulanmış kısmına (Supabase Free plan
  limitleri) dayanıyor.
- **Fiyatlandırma modellenmedi.** "Maliyet yapısı bize karşı" cümlesi
  yönlü bir akıl yürütme; sayısal bir birim ekonomi analizi yapılmadı.
- Kullanıcı görüşmesi yapılmadı; bu karar koddan ve rakip verisinden
  çıkarıldı.
