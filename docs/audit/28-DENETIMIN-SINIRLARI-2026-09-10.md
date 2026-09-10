# 28 — Denetimin sınırları: NE YAPILMADI

**Tarih:** 10.09.2026
**Kapsam:** 2026-09-09 tarihli kapsamlı denetim ve düzeltme turu

---

## Neden bu belge var

Bir denetim raporunun en tehlikeli tarafı, **yapılmamış olanı yapılmış
göstermesidir**. `docs/audit/` altındaki 27 rapor neyin bulunduğunu anlatıyor;
bu belge neyin **hiç bakılmadığını** anlatıyor.

Bir sonraki oturum, bu listedeki hiçbir maddeyi "denetimden geçti" saymamalı.

---

## Yapılmayanlar

| Yapılmadı | Neden |
|---|---|
| **Uygulama tıklanarak gezilmedi** | UX bulgularının tamamı kod okumasından — gerçek kullanılabilirlik testi değil |
| **Performans hiç ölçülmedi** | Yük testi yok, bundle analizi yok. Öneriler yapısal, ölçüm değil |
| **MSSQL + Oracle FK doğrulanamadı** | Docker VM'de 1904 MB var, MSSQL 2000 MB istiyor; Oracle imajı yok, disk 2.2 GB |
| **`frontend`'de hâlâ sıfır test** | 31.658 satır doğrulanmadan üretime gidiyor |
| **128 eslint hatası duruyor** | Yalnızca dokunduğum dosyalardakiler düzeltildi |
| **Rakip araştırması yüzeysel** | 3 rakip, yalnızca fiyat sayfaları — ürün turu, yorumlar, GitHub yok |

---

## Her maddenin ayrıntısı ve kapatma yolu

### 1. Uygulama tıklanarak gezilmedi

**Etkilenen raporlar:** `12-ui-ux-audit.md`, `14-user-scenarios.md`,
`13-accessibility-audit.md`

Bu üç rapor tamamen **kod okumasına** dayanıyor. Durum sinyalleri sayıldı
(`loading`, `empty`, `error` geçen satır sayısı), bileşen kodları okundu — ama
hiçbir ekran görülmedi.

**Bunun bir yanlış pozitif ürettiği ölçüldü:** "Desk'te hiç boş durum yok"
bulgusu yanlıştı; arama büyük harfle (`Empty`) yapılmıştı, CSS sınıfı `empty`.
Sayımla yapılan denetimin sınırı tam olarak budur.

**Kapatma yolu:** `14-user-scenarios.md` sonundaki 8 maddelik elle doğrulama
kontrol listesi. Yarım gün.

### 2. Performans hiç ölçülmedi

`08-performance-audit.md` **yapısaldır, ölçümsel değildir** ve bunu kendi
başlığında söylüyor.

Ölçülmeyenler:
- Bundle boyutu (`mermaid` ve `sql.js` önerisi paket boyutu genel bilgisine dayanıyor)
- Uç başına p95 gecikme (OpenTelemetry + Prometheus **kurulu**, yalnızca bakılmadı)
- 1M satırlık tabloda Gateway listesi davranışı
- Eşzamanlı yük altında bağlantı havuzu

**Tek ölçülen sayı:** küçük bir MariaDB veritabanının yedeği ~2,8 saniye
(canlı, `createdAt` → `completedAt` farkından).

**Kapatma yolu:** `@next/bundle-analyzer` (1 saat) + mevcut Prometheus
metriklerine bakmak (yarım gün).

### 3. MSSQL + Oracle FK introspection'ı doğrulanamadı

**Bu, listedeki en riskli madde.**

`DURUM.md` MySQL introspection'ında iki **gerçek** hata bulunduğunu kaydediyor:
FK ilişkileri hiç okunmuyordu ve `AUTO_INCREMENT` yanlış kolondan okunuyordu.
MySQL düzeltildi ve canlı doğrulandı.

**MSSQL ve Oracle aynı kod yolundan geçti ama hiç çalıştırılmadı.** Yani aynı
hata sınıfı o iki motorda **açık olabilir** ve ürün "6 motor destekliyoruz"
diyor.

**Neden yapılamadı:** ölçüldü, tahmin edilmedi —
- Docker VM toplam belleği **1904 MB**, SQL Server en az **2000 MB** istiyor
  (`--memory 4g` de çözmüyor, VM'in kendisi küçük)
- Oracle imajı yok ve disk **2.2 GB** boş

**Kapatma yolu:** Docker Desktop belleğini ≥ 3 GB'a çıkar, ardından
`RequiresEngineFact` testleri kendiliğinden koşar. Ya da CI'da (ubuntu-latest)
doğrula — orada bellek yeterli.

### 4. `frontend`'de sıfır test

31.658 satır, 146 dosya. `package.json`'da `test` script'i **yok**.

Karşılaştırma aynı depodan: `services/desk` 6.274 satır için **99 test** yazmış.
Aynı ekip, iki farklı standart.

Doğrulanmayan mantık yoğun alanlar:
- `useSchemaStore` (804 satır)
- `useProjectHistoryStore` (711 satır) — geri alma / sürüm çakışması
- `lib/templates.ts` (1.942 satır)

Üçü de **saf TypeScript** — React render'ı gerekmeden vitest ile test edilebilir.

### 5. 128 eslint hatası duruyor

`npm run lint` script'i **var** ama CI onu hiç çağırmıyor. Ölçülen:
`✖ 207 problems (129 errors, 78 warnings)`

Dokunduğum dosyalarda düzeltildi (`AIPreferencesModal` 10 → 1). Geri kalanı
duruyor.

**Çoğu `no-explicit-any` ama aralarında gerçek React hataları var:**
`react-hooks/set-state-in-effect`, "Cannot access variable before it is
declared", `react-hooks/immutability`.

**Sıra önemli:** CI'a şimdi eklenirse build anında kırmızıya döner ve ekip onu
devre dışı bırakmayı öğrenir — bu, hiç eklememekten kötüdür. Önce davranışsal
kuralları temizle, sonra kapıyı koy.

### 6. Rakip araştırması yüzeysel

**Toplanan:** Bytebase, Atlas, Supabase — yalnızca **resmî fiyat sayfaları**
(2026-09-09, tarayıcı paneliyle).

**Toplanmayan:**
- Ürün turları / dokümantasyon (özelliklerin gerçekte ne yaptığı)
- Kullanıcı yorumları, topluluk görüşü, GitHub issue'ları
- Liquibase, Flyway, Neon, PlanetScale, DBeaver, DataGrip, TablePlus
- Release notes (hangi özellik ne zaman geldi, yön nereye gidiyor)

**Bu yüzden cevaplanamayan kritik soru:**
> Bytebase'in "otomatik yedek + tek tıkla geri alma"sı geri yüklenebilirliği
> **doğruluyor mu**, yoksa yalnızca yedek alıp mı bırakıyor?

Namines'in en güçlü iddiası (`37. satır`, `17-feature-comparison.md`) buna
bağlı. Cevap "doğrulamıyor" ise ürünün ana mesajı budur; "doğruluyor" ise
farklılaştırıcı listesinden çıkmalı.

---

## Ek not: web araçları çalışmadı

`WebSearch` ve `WebFetch`, bu oturumda erişilemeyen bir modele
(`deepseek-v4-flash`) bağlı olduğu için sürekli hata verdi. Rakip verisi
**tarayıcı paneliyle** toplandı.

Bir sonraki oturumda web araçları çalışıyorsa araştırma çok daha hızlı
derinleşir.

---

## Özet — güven seviyeleri

| Alan | Güven |
|---|---|
| Backend güvenliği, mimari, veritabanı | **Yüksek** — kod okundu, testler koştu, bazıları canlı doğrulandı |
| Test durumu, CI, bağımlılıklar | **Yüksek** — ölçüldü |
| Frontend kod kalitesi | **Orta** — okundu, çalıştırılmadı, test edilmedi |
| UX, erişilebilirlik | **Düşük** — hiç görülmedi |
| Performans | **Düşük** — hiç ölçülmedi |
| MSSQL / Oracle desteği | **Düşük** — bilinen bir hata sınıfı doğrulanmadı |
| Rekabet konumu | **Orta** — fiyatlar kesin, özellik derinliği bilinmiyor |
