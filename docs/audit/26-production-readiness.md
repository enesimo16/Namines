# 26 — Üretime hazırlık değerlendirmesi

> ## ✅ DÜZELTMELERDEN SONRA — güncel puanlar
>
> | Alan | Denetimde | Şimdi | Ne değişti |
> |---|---|---|---|
> | Güvenlik | 62 | **84** | 4 P0 + CSRF + jeton iptali + kilitleme kapandı; 23 regresyon testi |
> | DevOps | 55 | **76** | `docker-compose.prod.yml`; CI'da bağımlılık taraması + atlanan test eşiği |
> | Güvenilirlik | 78 | **84** | Executor zaman aşımı/iptal; DDL kısmi-uygulama uyarısı; control DB retry |
> | Backend | 84 | **88** | Ortak hata sınıflandırıcı; executor denetim kaydı |
> | Veritabanı | 86 | **89** | Migration uygulamadan ayrıldı + bekleyen migration'da fail-fast |
> | Test | 70 | **73** | +23 test; frontend hâlâ 0 |
> | UX | 62 | **68** | Boş durumlar; kısmi uygulama uyarısı; sahte özellik kaldırıldı |
> | Frontend | 58 | **60** | Ölü kod ve lint hataları azaldı; test hâlâ yok |
> | Diğerleri | — | değişmedi | Mimari 85, Dokümantasyon 88, A11y 48, Performans 65 |
>
> ### **Kod kalitesi: 72 → 78** · **Üretime hazırlık: 64 → 79**
>
> **STOP SHIP listesinin 6/7'si kapandı.** Kalan tek madde bir belge:
> şifreleme anahtarı kurtarma planı (aşağıda).

Her puan **bulgularla gerekçelendirilmiştir**. Gerekçesiz puan yok.

---

## Kod kalitesi puanları

| Alan | Puan | Gerekçe |
|---|---|---|
| **Mimari** | **85** | Modül sınırı derleyiciyle korunuyor; asiklik referans grafiği; katmanlar net. −10: iki dev dosya (Gateway 1.447, Scaffolder 1.760). −5: Gateway'de yetki elle tekrarlanıyor. |
| **Backend** | **84** | 0 TODO, 0 uyarı, 0 ham EF SQL, tutarlı `CancellationToken`, Pipelines ile akış. −10: executor'ın yönetişim baypası. −6: hata politikası tutarsızlığı. |
| **Frontend** | **58** | Modern yığın, Zustand ile düzenli store'lar, 193 hata durumu. −25: **sıfır test**. −10: 1.521 satırlık modal + sahte özellik. −7: bundle ölçülmemiş. |
| **Veritabanı** | **86** | 24 indeks doğru yerlerde; tutarlı identifier allowlist'i; motor başına doğru sayfalama; golden-file DDL koruması. −10: migration deploy'a bağlı. −4: MySQL DDL transaction yanılsaması. |
| **Güvenlik** | **62** | SSRF guard, PII redaksiyonu, identifier allowlist'i, httpOnly cookie, fail-closed şifreleme anahtarı — hepsi var. −20: 4 adet P0 (SEC-001/003/004/005). −10: CSRF açığı cross-site'ta. −8: jeton iptali ve lockout yok. |
| **Test** | **70** | 1.480 backend testi gerçek konteynerlere karşı; golden-file; gerekçeli atlama. −25: frontend 0 test. −5: E2E yok. |
| **Performans** | **65** | Pipelines ile bellek güvenliği, N+1 çözülmüş, zaman aşımları tanımlı. −20: **hiç ölçülmemiş**. −10: yedek senkron, bundle optimize edilmemiş, executor zaman aşımsız. |
| **Güvenilirlik** | **78** | Arıza yolları düşünülmüş ve çoğu kanıtlanmış; çoklu instance güvenli zamanlayıcı; fail-closed davranışlar. −12: optimistic concurrency yok. −10: retry ve kaynak limiti yok. |
| **DevOps** | **55** | CI gerçek testler koşuyor, health check'ler var, sırlar temiz. −30: **üretim dağıtım tanımı yok**. −15: TLS/proxy belirsiz, rollback yok, güvenlik taraması yok. |
| **UX** | **62** | Hata durumları yoğun işlenmiş, onboarding rotaları var, Vault onayı örnek. −20: Desk'te boş durum yok. −10: iki uygulama arası kopukluk. −8: sahte özellik. |
| **Erişilebilirlik** | **48** | Radix UI iyi bir zemin, 151 aria kullanımı. −30: 51 input'a 5 `htmlFor`. −15: hiç denetlenmemiş (kontrast/klavye/ekran okuyucu). −7: canvas için alternatif belgelenmemiş. |
| **Dokümantasyon** | **88** | Kanıt seviyeleri, gerekçeli kararlar, kayıtlı başarısızlıklar, örnek `.env.example`. −8: üretim kılavuzu yok. −4: belge gezinmesi karışık. |
| **Bakım yapılabilirlik** | **82** | 0 TODO, gerekçeli yorumlar, düşük teknik borç, düşük kuplaj. −10: iki dev dosya. −8: frontend testsiz. |
| **Ölçeklenebilirlik** | **72** | Durumsuz API, Redis backplane, çoklu instance güvenli işler, akış tabanlı yedek. −15: migration açılışta. −8: kaynak limiti/otomatik ölçekleme tanımsız. −5: ölçülmemiş. |

---

## Ağırlıklı genel puan

Ağırlıklar, ürünün **veritabanı erişim ürünü** olmasına göre belirlendi:
güvenlik ve güvenilirlik ağırlıklı.

| Alan | Puan | Ağırlık | Katkı |
|---|---|---|---|
| Güvenlik | 62 | 18% | 11,2 |
| Güvenilirlik | 78 | 12% | 9,4 |
| Backend | 84 | 12% | 10,1 |
| Veritabanı | 86 | 10% | 8,6 |
| Test | 70 | 10% | 7,0 |
| DevOps | 55 | 8% | 4,4 |
| Mimari | 85 | 7% | 6,0 |
| Frontend | 58 | 7% | 4,1 |
| UX | 62 | 6% | 3,7 |
| Performans | 65 | 4% | 2,6 |
| Dokümantasyon | 88 | 3% | 2,6 |
| Bakım | 82 | 2% | 1,6 |
| Ölçeklenebilirlik | 72 | 1% | 0,7 |
| **TOPLAM** | | **100%** | **71,9** |

# **Kod kalitesi: 72 / 100**

---

## Üretime hazırlık puanı

Bu ayrı bir soru: *"Bugün gerçek kullanıcılar bunu kullanabilir mi?"*

| Boyut | Puan | Gerekçe |
|---|---|---|
| **Güvenlik** | **60** | 4 adet P0 açık. Hiçbiri "kapıyı ardına kadar açık bırakan" türden değil ama SEC-005 yanlış yapılandırmada tam baypas veriyor. |
| **Kararlılık** | **78** | Çökmüyor, arıza yolları düşünülmüş, zaman aşımları var. Executor istisna. |
| **Dağıtılabilirlik** | **45** | **Üretim tanımı yok.** Bugün çalışıyorsa elle kurulmuş demektir; yeniden kurulamaz. |
| **Gözlemlenebilirlik** | **75** | OpenTelemetry + Prometheus + Serilog + PII redaksiyonu + korelasyon ID kurulu. −25: alarm (alerting) tanımı yok, denetim kaydı iki kritik uçta eksik. |
| **Felaket kurtarma** | **55** | Vault kullanıcının DB'sini koruyor ve **geri yüklenebilirliğini kanıtlıyor** (güçlü). Ama **control DB'nin kendi yedeği** belgelenmemiş; RPO/RTO tanımsız; restore tatbikatı yok. |
| **Veri güvenliği** | **80** | Bağlantı dizeleri şifreli, yedekler AES-256-GCM, anahtarlar ayrı, fail-closed. −20: XSS yolu ve CSRF açığı. |
| **UX** | **62** | Kullanılabilir ama ilk deneyimde boşluklar var. |
| **Uyum (a11y/gizlilik)** | **50** | Gizlilik/şartlar sayfaları var, PII redaksiyonu var. Erişilebilirlik denetlenmemiş. |

# **Üretime hazırlık: 64 / 100**

---

## Felaket kurtarma — ayrı not

Denetim bunu özellikle aradı çünkü ürün yedekleme iddiasında.

| Soru | Cevap |
|---|---|
| Kullanıcının DB'si yedekleniyor mu? | ✅ Vault — PG/MySQL/MariaDB canlı doğrulandı |
| Yedek şifreli mi? | ✅ AES-256-GCM |
| Yedek nesne deposunda mı? | ✅ S3/MinIO desteği, gerçek MinIO'ya karşı doğrulandı |
| **Geri yükleme test ediliyor mu?** | ✅ **Evet — temiz sunucuya gerçek restore.** Bu ender ve güçlü. |
| **Control DB yedekleniyor mu?** | ❓ **Belgelenmemiş** |
| **Uygulama yapılandırması yedekli mi?** | ❌ Dağıtım tanımı depoda yok |
| **Sır kurtarma planı** | ❌ Yok — `Vault:BackupEncryptionKey` kaybolursa **tüm yedekler okunamaz** |
| RPO / RTO | ❌ Tanımsız |
| Restore tatbikatı | ❌ Yapılmamış |

### En kritik boşluk
**`Vault:BackupEncryptionKey` kaybolursa her yedek kalıcı olarak okunamaz hâle
gelir.** Bu anahtarın nerede, kaç kopya olarak saklandığı hiçbir yerde yazılı
değil. Yedekleme sistemi kurmanın en acı ironisi budur: yedek var, anahtar yok.

**Öneri (P0, XS):** Anahtar saklama ve kurtarma prosedürünü yaz. Anahtarın en az
iki bağımsız yerde (ör. parola yöneticisi + kapalı zarf) durduğunu doğrula.

---

## STOP SHIP listesi

Üretime çıkmadan önce **kesinlikle** çözülmesi gerekenler:

| # | Bulgu | Neden STOP SHIP | Durum |
|---|---|---|---|
| 1 | **SEC-005** JWT fallback anahtarı | Yanlış ortam adı → tam kimlik baypası | ✅ Kapandı |
| 2 | **SEC-004** Mermaid XSS | Paylaşılan şema başkasının tarayıcısında kod çalıştırabilir | ✅ Kapandı |
| 3 | **SEC-003** Sahte jeton | Tutulmayan güvenlik sözü | ✅ Kapandı |
| 4 | **SEC-001** Denetimsiz keyfi SQL | Veri kaybı + adli inceleme imkânsız | ✅ Kapandı |
| 5 | **AUTHZ-004** CSRF (cross-site dağıtımda) | Önerilen dağıtım şekli tam da bu | ✅ Kapandı |
| 6 | **DEVOPS-001** Üretim tanımı yok | Sistem yeniden kurulamıyor | ✅ Kapandı |
| 7 | **Şifreleme anahtarı kurtarma planı** | Anahtar kaybı = tüm yedeklerin kaybı | 🟡 **AÇIK** |

### Kalan tek STOP SHIP maddesi

`Vault:BackupEncryptionKey` kaybolursa **her yedek kalıcı olarak okunamaz hâle
gelir.** Bu anahtarın nerede, kaç kopya olarak saklandığı hiçbir yerde yazılı
değil.

Bu bir kod işi değil, bir **prosedür** işi ve bu yüzden burada bırakıldı:
anahtarın nerede durduğunu yalnızca onu üreten kişi bilir. Yazılması gereken:

1. Anahtar nerede üretildi ve kim üretti?
2. Kaç bağımsız kopyası var? (En az iki: ör. parola yöneticisi + çevrimdışı kopya)
3. Kaybolursa ne olur? (Cevap: geçmiş yedeklerin tamamı okunamaz)
4. Değiştirilmesi gerekirse ne yapılır? (Eski anahtar, eski yedekleri açmak
   için SAKLANMALI — yenisi yalnızca yeni yedekler için)

Bu dört soruya bir sayfalık cevap, yedekleme sisteminin en acı ironisini
önlüyor: yedek var, anahtar yok.
