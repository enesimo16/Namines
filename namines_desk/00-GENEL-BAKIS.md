# Namines Desk v1 — Genel Bakış

> **Durum:** plan. v0.1 (deterministik CRUD) çalışıyor ve
> [`services/desk/`](../services/desk/) altında; bu klasör **v1'in** kapsamını
> tanımlıyor.
>
> Ana faz dokümanı: [`third-phase/00-BASLA-BURADAN.md`](../third-phase/00-BASLA-BURADAN.md)

---

## 1. Desk nedir, ne değildir

**Namines** şemayı *tasarlar*. **Desk** o şemanın arkasındaki **veriyi yönetir**.

Aynı uygulamada olmamalarının sebebi kozmetik değil: farklı kullanıcı (tasarımcı
vs. operatör), farklı oturum süresi, farklı dağıtım ritmi. Desk ayrı bir
mikroservis — kendi portu, kendi imajı, ana koda **sıfır referans**
([§5, third-phase](../third-phase/00-BASLA-BURADAN.md)).

**Desk DEĞİLDİR:** barındırma sağlayıcısı. Kullanıcının *uygulamasını* değil,
yalnızca kendi arayüzünü barındırıyoruz. `second-phase/14`'teki barındırma
reddi hâlâ geçerli.

---

## 2. Tasarım referansı: Vercel

Vercel'in kontrol paneli **düzen ve etkileşim** açısından referans alınıyor:
sol kalıcı gezinme, üstte proje seçici, kart ızgarası, sağda içerik.
Ayrıntılar [`08-TASARIM-SISTEMI.md`](08-TASARIM-SISTEMI.md)'de.

### ⚠️ Kopyalanmayacak olan: metrikler

Vercel'in ana ekranındaki sayılar **kendi altyapısından** gelir: Edge Requests,
Fluid Active CPU, Fast Origin Transfer, ISR Reads. Namines'te bunların
**karşılığı yok** — biz kimsenin uygulamasını çalıştırmıyoruz.

Bu kutuları benzer isimlerle doldurmak **uydurma sayı göstermek** olurdu.
Desk'in Analytics ekranı, ürünün gerçekten ürettiği veriye dayanacak
([`07-ANALYTICS.md`](07-ANALYTICS.md)).

**Kural:** Vercel'den *düzen* alınır, *veri* alınmaz.

---

## 3. Elimizde gerçekten ne var (doğrulandı)

Aşağıdakiler koda bakılarak doğrulandı, varsayım değil.

| Yetenek | Kaynak | Durum |
|---|---|---|
| Kimlik doğrulama (JWT) | `POST /api/auth/login` | ✔ çalışıyor |
| Kullanıcının projeleri | `GET /api/auth/projects` | ✔ çalışıyor |
| Proje şeması + **canvas konumları** | `CloudProject.SchemaJson`, `NodePositionsJson` | ✔ var |
| Şifreli canlı DB bağlantısı | `CloudProject.EncryptedConnectionString` | ✔ v0.1'de eklendi |
| Canlı şema keşfi | `GET /api/gateway/schema` | ✔ v0.1'de eklendi |
| CRUD | `/api/gateway/{list,detail,create,update,delete}` | ✔ çalışıyor |
| **Proje bazlı yazma denetim kaydı** | `GatewayAuditEntry` (`ProjectId` taşıyor) | ✔ var, arayüzü yok |
| Şema sürüm geçmişi | `Branch` + `SchemaVersion` | ✔ var, arayüzü yok |
| Değişiklik incelemesi | `ChangeRequest` + `ImpactReport` | ✔ var, arayüzü yok |
| API anahtarı + tablo izinleri | `GatewayApiKey` | ✔ çalışıyor |

### Elimizde OLMAYAN — dürüstlük tablosu

| İstenen | Gerçek durum | Sonuç |
|---|---|---|
| Okuma istekleri logu | `GatewayAuditEntry` **yalnızca yazma** kaydeder (`Create/Update/Delete/Import/Rpc/Sql`) | Logs ekranı "yazma işlemleri" der, "tüm istekler" demez |
| Proje bazlı istek sayacı | `UsageEvent` **kullanıcı** bazlı (`UserId`), proje alanı yok | Ya şema değişikliği gerekir ya proje bazlı sayım audit'ten türetilir |
| CPU / transfer / edge metrikleri | `NaminesMetrics` OpenTelemetry sayaçları — DB'de sorgulanabilir değil, proje bazlı değil | Desk'te **gösterilmeyecek** |
| GitHub push entegrasyonu | `GithubBotService` + webhook var ama Desk'e bağlı değil | v1 kapsamı dışı, [`05`](05-DEPLOYMENTS.md)'te not düşüldü |

---

## 4. En büyük tasarım kararı: anahtar kaybolmalı

**Sorun (kullanıcı geri bildirimi):** *"şu an key nereden alınacak belli değil."*

v0.1'de kullanıcı elle bir Gateway API anahtarı yapıştırıyor. Bu yanlış:
anahtarlar **dış uygulamalar** için tasarlandı, insan için değil. Üstelik ham
anahtar yalnızca üretildiği anda görünüyor — kullanıcı kaybederse geri alamıyor.

**Çözüm:** Desk **JWT ile** çalışır, anahtarla değil.

Gateway zaten iki kimlik yolunu destekliyor (`AuthorizeAsync`): API anahtarı
**veya** oturum. Oturum yolunda tablo izinleri uygulanmaz — proje sahibi kendi
verisinin tamamını zaten görebilir, bu doğru davranış.

Tek eksik: oturum yolunda hangi projenin bağlantısının kullanılacağı belli değil
(bugün bu bilgi anahtardan geliyor). Ayrıntı ve gereken küçük backend değişikliği
[`01-KIMLIK-VE-OTURUM.md`](01-KIMLIK-VE-OTURUM.md)'de.

**Sonuç:** kullanıcı Desk'e girer, giriş yapar, projesini seçer. Anahtar diye bir
kavramla hiç karşılaşmaz. API anahtarları yerinde kalır — ama artık gerçek
sahibine, yani **dış uygulamalara** ait olur.

---

## 5. Ekranlar

| # | Ekran | Doküman | Dayandığı veri |
|---|---|---|---|
| 1 | Projects (ana ekran) | [`02-PROJECTS.md`](02-PROJECTS.md) | `GET /api/auth/projects` |
| 2 | Project → Canvas | [`03-CANVAS.md`](03-CANVAS.md) | `SchemaJson` + `NodePositionsJson` |
| 3 | Data (CRUD) | [`04-DATA-CRUD.md`](04-DATA-CRUD.md) | `/api/gateway/*` — **v0.1'de çalışıyor** |
| 4 | Deployments | [`05-DEPLOYMENTS.md`](05-DEPLOYMENTS.md) | `SchemaVersion`, `ChangeRequest` |
| 5 | Logs | [`06-LOGS.md`](06-LOGS.md) | `GatewayAuditEntry` |
| 6 | Analytics | [`07-ANALYTICS.md`](07-ANALYTICS.md) | `GatewayAuditEntry` toplamları |
| — | Tasarım sistemi | [`08-TASARIM-SISTEMI.md`](08-TASARIM-SISTEMI.md) | — |
| — | Sıra ve kabul kriterleri | [`09-YOL-HARITASI.md`](09-YOL-HARITASI.md) | — |

---

## 6. Değişmeyen kurallar

1. **Mikroservis sınırı.** `Namines.Core`'a ya da `frontend/`'e kod referansı
   YOK. Yalnızca HTTP. Tipler bilinçli kopya.
2. **Kanıt = çalışan komut + görülen çıktı.** "Test geçti" tek başına yeterli
   değil; her ekranın kabul kriteri [`09`](09-YOL-HARITASI.md)'da yazılı.
3. **Uydurma veri yok.** Bir sayının kaynağı yoksa o kutu **çizilmez**.
   Boş bırakmak, yanlış doldurmaktan iyidir.
4. **Yürüyen iskelet önce.** Her ekranda tek bir uçtan uca yol kanıtlanmadan
   ikinci özellik yazılmaz.
