# 14 — AYRI ÜRÜN: Development ve Hosting

> ⚠️ **Bu Namines değil.** Ayrı bir ürün, ayrı bir ekip, ayrı bir iş modeli.
> Buraya yazılmasının sebebi fikri kaybetmemek — **şimdi başlanacak diye değil.**

---

## Fikir

Üç parça, giderek ağırlaşan:

1. **Development paketleri** — bugünkü eject'in genişletilmiş hâli. Basitten
   kapsamlıya 3-4 seçenek, paketlenmiş
2. **Barındırma** — `proje-adi.namines.com` altında çalışan uygulama
3. **Dağıtım merkezi** — Shopify benzeri; kullanıcılar uygulamalarını yayınlar

## Değerlendirme

| Parça | Karar | Neden |
|-------|-------|-------|
| **1. Development paketleri** | 🟢 **Namines içinde yapılabilir** | Eject zaten var (18 hedef). Paketleme ve kalite artışı doğal genişleme, yeni altyapı gerekmiyor |
| **2. Barındırma** | 🔴 **Ayrı ürün, şimdi değil** | Aşağıda |
| **3. Dağıtım merkezi** | 🔴 **Ayrı şirket** | Aşağıda |

## Barındırma neden şimdi değil

Barındırma seni **araç satıcısından altyapı sağlayıcısına** çevirir. Bu bir
özellik farkı değil, **iş modeli değişimi**:

- **7/24 nöbet.** Kullanıcının uygulaması gece 3'te düşerse senin sorunun
- **Kötüye kullanım.** Birileri kripto madenciliği, spam, yasadışı içerik
  barındıracak. Abuse ekibi gerekiyor
- **Yasal sorumluluk.** Barındırdığın içerikten sorumlusun. GDPR/KVKK, veri
  yerleşimi, mahkeme talepleri
- **Maliyet yapısı ters.** Namines'te maliyet kullanımla artıyor ama tahmin
  edilebilir; barındırmada boşta duran kaynak da para yakıyor
- **Yedekleme ve kurtarma.** Veri kaybı ürünü bitirir

Shopify'ın 8.000+ çalışanı var ve işinin çoğu bu. **Tek geliştiriciyle
barındırmaya girmek, Namines'in kendisini durdurur.**

## Ne zaman geri dönülür

Üçü birden olduğunda:
1. Namines'in **ödeyen ve büyüyen** bir müşteri tabanı var
2. Müşteriler **kendileri** "bunu bir de siz çalıştırın" diye soruyor
3. Nöbeti devralacak en az bir kişi daha var

Öncesinde: kullanıcıyı **kendi** barındırıcısına yönlendir (Vercel, Railway,
Fly.io, Neon). Üretilen paket oralara hazır gitsin — bu, barındırma
sorumluluğunu almadan aynı ihtiyacın çoğunu karşılıyor.

## ⚠️ Dikkat

- Development paketleri (1) Namines içinde kalabilir ama **isim ayrımı**
  yapılmalı; "Namines size uygulama da yazar" izlenimi, ürünün odağını dağıtır.
- Bu doküman bir **plan değil**, bir kayıt. Sıradaki işler [01](01-SIRADAKI-ISLER.md)'de.

## 🔴 Yapılmayacak (şimdilik)

- Herhangi bir barındırma altyapısı kurmak
- `*.namines.com` alt alan adı yönlendirmesi
- Kullanıcı kodunu sunucularımızda çalıştırmak — bu, izole çalıştırma
  (sandboxing) demek ve `docker.sock` kuralının neden var olduğuyla aynı
  sınıfta bir risk
