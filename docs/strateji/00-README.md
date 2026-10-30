# Strateji — Namines'in sonraki dönemi

> Masaüstü (Tauri v2), tarayıcı eklentisi, mobil güvenlik uygulaması ve
> Supabase/Firebase başta olmak üzere tüm sağlayıcı entegrasyonları için
> planlar. Her plan ayrı bir dosya. Okuma sırası aşağıdaki gibi.
>
> Yazıldı: 2026-10-30, Ekim denetiminden hemen sonra. Önceki ekosistem
> planı [second-phase/18-EKOSISTEM-...](../second-phase/18-EKOSISTEM-TICARI-VE-BUYUME-PLANI.md)
> **ne** yapılacağını anlatıyordu. Bu klasör **nasıl, hangi sırayla ve
> hangi kabul kriterleriyle** yapılacağını anlatıyor; çeliştikleri yerde
> bu klasör güncel kabul edilir.

## Tek cümlede strateji

**Namines'in kaması "veritabanı değişikliğini güvenle canlıya çıkarmak".**
Her yeni platform bu kamaya bir işle hizmet eder:
- **masaüstü** (Tauri), web'in ulaşamadığı veritabanına ulaşır ve Namines'in tamamını yönetir;
- **eklenti**, Supabase/Firebase'den şemayı ve veriyi otomatik çeker ve Namines'i her an yönetmeyi sağlar. Bunu **onlarla entegre** yapar, önlerine geçmeye çalışmaz;
- **mobil**, DB'yi yönetir, kapatır, talimatları işler, analiz edip sunar ve kritik işlemin ikinci anahtarıdır;
- **entegrasyonlar ve OAuth** bu üçünün içeriğidir: sistemi ileri taşıyan her sağlayıcı bağlanır.

> **2026-10-30 güncellemesi** (ürün sahibinin yönlendirmesi): OAuth kataloğu
> eklendi (09). Eklenti tam yönetim + otomatik çekme olarak genişletildi
> (04). Mobil yönetim + güvenlik olarak genişletildi, Expo kararı gerekçelendi
> (05). Masaüstü MCP'ye değil ortak **Namines Engine**'e bağlandı (10).

## Dosyalar

| # | Dosya | Konu |
|---|---|---|
| 01 | [PROJE-DEGERLENDIRMESI](01-PROJE-DEGERLENDIRMESI.md) | Bugün neredeyiz: güçlü/zayıf yanlar, yeni platformlardan önceki kurallar |
| 02 | [ORTAK-TEMEL](02-ORTAK-TEMEL.md) | Üç platformun önkoşulu: OAuth 2.1 sunucusu, device flow, passkey, cihaz kaydı, push, `/api/v1`, SDK, imza ve mağazalar |
| 03 | [MASAUSTU-TAURI](03-MASAUSTU-TAURI.md) | Namines Desktop: yerel/VPN DB erişimi, `namines-agent` sidecar, izin modeli, köprü |
| 04 | [TARAYICI-EKLENTISI](04-TARAYICI-EKLENTISI.md) | Namines Companion: Supabase/Firebase'den otomatik şema+veri çekme, eklentiden tam Namines yönetimi, GitHub PR farkı |
| 05 | [MOBIL](05-MOBIL.md) | Namines Mobile: DB yönetimi ve kapatma, talimat → plan → onay, analiz kartları, imzalı onay, Expo kararı |
| 06 | [ENTEGRASYON-MIMARISI](06-ENTEGRASYON-MIMARISI.md) | `Namines.Connectors` çerçevesi: yetenek modeli, token kasası, IR eşleme, uygunluk kiti |
| 07 | [ENTEGRASYON-KATALOGU](07-ENTEGRASYON-KATALOGU.md) | Supabase, Neon, Firebase, PlanetScale, D1, Turso, MongoDB, AWS, GCP, Azure... öncelik ve efor |
| 08 | [YOL-HARITASI](08-YOL-HARITASI.md) | Fazlar, kapılar, risk kaydı ve **senden beklenen kararlar** |
| 09 | [OAUTH-KATALOGU](09-OAUTH-KATALOGU.md) | Giriş (GitHub, Google, Microsoft, Apple, LinkedIn, GitLab, SSO) + bağlantı (Supabase, Firebase, Cloudflare, Neon, Vercel, Azure, Slack...) sağlayıcıları, scope'lar, hesap birleştirme kuralları |
| 10 | [MOTOR-VE-MCP-MIMARISI](10-MOTOR-VE-MCP-MIMARISI.md) | Tek motor, üç yüzey: masaüstü ajanı (tam kapsam), IDE MCP (profilli, 13 → ~45 araç), Remote MCP |
| 11 | [KARAR-KAYDI](11-KARAR-KAYDI.md) | Strateji konuşmasının kalıcı kaydı: sorular, yönlendirmeler, cevaplar, ölçümler (MCP %6 kapsam, DB 44 tablo) ve kararlar |

## Hızlı okuma (10 dakika)

1. [01 §4](01-PROJE-DEGERLENDIRMESI.md#4-sonuç-ve-öneri-üç-platform-kararına-nasıl-bakmalı): üç platformun her biri neye hizmet eder.
2. [08 — Genel sıra](08-YOL-HARITASI.md#genel-sıra): fazlar.
3. [08 — Senden beklenen kararlar](08-YOL-HARITASI.md#senden-beklenen-kararlar).
