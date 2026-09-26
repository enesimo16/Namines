# Launch — Desk / Vault / Ground / kod projesi entegrasyonu

**Tarih:** 2026-09-13
**Durum:** Onaylandı (brainstorming), plan aşamasına geçiliyor

> ✅ **Bitmiştir.** Plan ([`../plans/2026-09-14-launch-integration.md`](../plans/2026-09-14-launch-integration.md))
> dokuz görevin tamamıyla uygulandı: `LaunchService`, `LaunchController`
> (`POST /api/launch`, `POST /api/launch/{projectId}/download`),
> `frontend/components/compile/LaunchPanel.tsx`, Desk handoff derin bağlantısı.
> (2026-09-26'da koddan doğrulandı.)
**Kapsam:** Bu doküman yalnızca "Launch" omurgasını kapsar. Namines Flow (olay
kaynağı + bildirim) **bu spec'in dışında** — kullanıcının kendi ifadesiyle
sıradaki, ayrı bir spec.

---

## 1. Problem

Namines'te üç ürün var ve şu an birbirinden kopuk:

- **Ground** — canlı veritabanı açar (LocalPostgres/Neon/Supabase), canlı kanıtlanmış.
- **Vault** — yedekler, canlı kanıtlanmış (PostgreSQL/MySQL/MariaDB).
- **Desk** — bir projenin CRUD/SQL/denetim paneli.
- **Scaffolder/Eject** — `/compile`'da bağımsız bir kod projesi üretir.

Kullanıcı `/compile`'da bir şemayı onayladıktan sonra bu dördünü birbirine
bağlayan hiçbir yol yok: Ground'da veritabanı açmak, DDL'i ona uygulamak,
ilk yedeği almak ve Desk panelini/kod projesini o veritabanına bağlı olarak
görmek — hepsi elle, ayrı ayrı, doğru sırayı bilerek yapılması gereken adımlar.

**Hedef:** `/compile`'da tek bir "Launch" eylemi; saniyeler içinde ya çalışan
bir Desk paneli ya da bağlantısı hazır bir kod projesi (ya da ikisi).

---

## 2. Omurga — sunucu tarafı orkestrasyon

Yeni uç nokta: `POST /api/launch`

```
Request:  { projectId: string, provider: string }
          (provider: Ground'un mevcut /api/ground/providers listesinden biri)
```

### Adımlar (sunucu, tek istek — arayüz adım adım gösterir, bkz. §4)

1. **Proje** — `projectId` yoksa, mevcut `/api/auth/sync` deseniyle boş bir
   `CloudProject` oluşturulur (`schemaJson: "{}"`, kullanıcının kişisel org'una
   bağlı). Zaten varsa dokunulmaz.
2. **Ground provizyon** — `GroundService.ProvisionAsync` (mevcut, değişmiyor).
   Sağlayıcı `ProbeAsync`'te düşerse burada dur, DDL'e hiç geçme (bkz. §5).
3. **Hedef introspection** — `DbIntrospectionService` ile hedef veritabanı
   okunur (mevcut, Desk'in şema ekranının kullandığı aynı servis).
   - **Tablo sayısı 0 ise** → adım 4a.
   - **Tablo sayısı > 0 ise** → adım 4b.
4a. **Doğrudan uygula** — `IDatabaseExecutor.ExecuteScriptAsync` ile DDL
    hedefe yazılır. `DatabaseExecutorController.ExecuteScript`'in yaptığı gibi
    bir `SqlExecutionAudit` satırı burada da yazılır (aynı desen, kopyalanır —
    controller'da yaşıyor, serviste değil).
    → adım 5.
4b. **Change Review'a devret** — `ChangeRequestController.CreateQuick`
    (`QuickChangeRequestRequest { ProjectId, SchemaJson, Title }`) çağrılır.
    DDL uygulanmaz. Yanıt `{ status: "NeedsReview", reviewUrl: "/review/{id}" }`
    olur ve akış burada biter — mevcut Review/onay akışı devam eder.
5. **İlk Vault yedeği** — `VaultService`'in mevcut manuel yedek yolu
   (`POST /api/vault/{projectId}/backups` ile aynı iç çağrı) tetiklenir.
   Başarısız olursa adım 6'yı ENGELLEMEZ, yalnızca uyarı taşınır (bkz. §5).
6. **Yanıt** —
   ```
   { status: "Ready",
     deskUrl: "http://localhost:3200/handoff?...&project={id}&view=data",
     backupWarning: string | null }
   ```

### Neden "ilk kurulum mu / güncelleme mi" bir istemci bayrağı DEĞİL

Karar canlı introspection'dan çıkıyor (adım 3). Böylece:
- Desk'ten ya da elle tablo eklenmiş bir hedefe Launch tekrar çalıştırılırsa
  yine doğru dala düşer.
- İstemci hiçbir "mod" göndermez; sunucu tek doğruluk kaynağı.

---

## 3. Kod projesi çıktısı — Gateway anahtarıyla, ham parola YOK

`ScaffolderController.ExportProject` bugün yalnızca şema alıyor, bağlantı
bilgisi yok. Kullanıcı hem Desk panelini hem kod projesini istediği için bu
uca da bağlanıyoruz — ama **ham veritabanı parolası hiçbir zaman bir zip'e
girmez** (indirilenler klasöründe kalır, yanlışlıkla paylaşılabilir, iptal
edilemez).

Yerine: Launch tamamlandığında (adım 6), kullanıcı "Download project" derse:

1. `GatewayKeyController.Create` ile o proje için otomatik bir anahtar
   açılır (`CreateGatewayKeyRequest { Name: "Scaffolded project", CanWrite: true }`).
2. Scaffolder'ın ürettiği `.env`/`appsettings.json` şablonuna ham connection
   string yerine `NAMINES_GATEWAY_URL` + `NAMINES_GATEWAY_KEY` yazılır.
3. Üretilen istemci kodu (`FrontendSdkScaffold` zaten TypeScript SDK
   üretiyor) Gateway API'ye karşı konuşur — doğrudan veritabanına değil.

Anahtar sonradan Desk'in API Keys ekranından tek tıkla iptal edilebilir
(mevcut `Revoke` ucu). Bu, projenin zaten var olan güvenlik modeliyle
(tablo bazlı, iptal edilebilir, denetimli) birebir örtüşüyor.

---

## 4. Arayüz — `/compile`'da "Launch" ve adım checklist'i

- DDL Script sekmesinin yanına bir "Launch" düğmesi (mevcut "Namines Desk /
  Ground / Vault" düğmeleriyle aynı sidebar bloğunda, `PanelKit` bileşenleri).
- Tıklanınca küçük bir panelde sağlayıcı seçimi (Ground'un `/api/ground/providers`
  listesi, zaten `/ground` sayfasında kullanılan desen).
- Onaylanınca adım adım checklist:
  ```
  ⏳ Opening database…
  ○  Applying schema…
  ○  Taking first backup…
  ○  Ready
  ```
  Her adım sunucudan gelen tek yanıtın ÜZERİNE değil — istemci adımları
  SIRAYLA ayrı çağrılarla değil, **tek `/api/launch` çağrısının kendisi
  senkron olarak tüm adımları yapıp sonunda döner**; checklist bu bekleme
  süresince iyimser ilerler (her adımın tahmini süresi biliniyor, `Vault`'un
  "yedekleme arka planda" 202 deseninden farklı olarak burada tüm zincir
  saniyeler sürdüğü için tek istekte tutuluyor). Hata olursa checklist tam
  o adımda durur ve kırmızıya döner (bkz. §5) — "sonunda spinner + tek
  mesaj" değil.
- Sonuç `Ready` ise: "Open Desk" (yeni sekme, `deskUrl`) ve "Download project"
  (Gateway anahtarlı zip) iki buton yan yana.
- Sonuç `NeedsReview` ise: "Review changes" butonu `reviewUrl`'e götürür,
  checklist "Şema veritabanında zaten tablo var — Review'a gönderildi" notuyla
  biter (DDL hiç uygulanmadı, kullanıcı yanlış anlamasın diye açıkça yazılır).

---

## 5. Hata yönetimi

| Adım | Hata | Davranış |
|---|---|---|
| Ground provizyon | Sağlayıcı yapılandırılmamış/probe düştü | Dur, hangi sağlayıcı olduğunu göster, DDL'e hiç geçme |
| DDL uygulama | Transactional olmayan motorda (MySQL/MariaDB/Oracle) kısmi uygulama | `ExecuteScriptAsync`'in döndürdüğü `StatementsExecuted` sayısı gösterilir; "yeniden dene" (DDL'ler `IF NOT EXISTS` kullandığı için idempotent) ya da Ground'un mevcut sil akışıyla temizleme sunulur |
| İlk Vault yedeği | Başarısız | **Engellemez** — `Ready` durumuna geçilir ama `backupWarning` alanı dolu döner, arayüzde görünür kırmızı/turuncu uyarı olarak kalır, sessizce yutulmaz |
| Zip/Gateway anahtarı | Başarısız | Desk paneli yolunu etkilemez; "Download project" ayrı yeniden denenebilir |

---

## 6. Test

- **Backend birim:** `LaunchController` — mock `IGroundService`/`IDatabaseExecutor`/
  `IMigrationService`/`IVaultService` ile iki dal: boş hedef → doğrudan uygula;
  dolu hedef → `ChangeRequest` oluşur, DDL hiç çağrılmaz. Transactional olmayan
  motorda kısmi hata senaryosu ayrı test.
- **Entegrasyon (Testcontainers, mevcut desen):** Bir uçtan-uca canlı test —
  LocalPostgres provizyon → DDL uygula → bağımsız `psql` ile tablo kontrolü →
  `VaultBackups` tablosunda bir satır oluştuğunu doğrula.
- **Frontend (vitest):** Checklist bileşeninin adım geçişleri (pending →
  success/error) ve `NeedsReview` dalının doğru mesajı gösterdiği.

---

## 7. Kapsam dışı (bilinçli)

- **Namines Flow** (olay kaynağı + webhook/e-posta bildirimi) — ayrı spec,
  kullanıcının kendi sıralamasıyla Launch'tan SONRA.
- Launch'ın kendisini zamanlanmış/tekrarlayan bir işe bağlamak — Flow'un işi.
- Kod projesi çıktısının Desk dışında bir yerde (örn. GitHub'a otomatik push)
  barındırılması — istenmedi, kapsam dışı.
