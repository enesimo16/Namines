# Migration kuralı ve geri alma prosedürü

**Son güncelleme:** 10.09.2026 · Kaynak bulgu: `DEVOPS-005`
(bkz. [10-devops-deployment-audit.md](../audit/10-devops-deployment-audit.md))

---

## 1. Neden bir kural gerekiyor

Geri alma (rollback), kodu eski sürüme döndürmektir. **Veritabanını
döndürmez.** Sonuç şu: geri alındıktan sonra **eski kod, yeni şemaya karşı
çalışmak zorunda kalır.**

Bu, iki şeyden birinin doğru olmasını gerektirir:

- Ya her migration eski kodla da çalışabilir hâlde yazılır,
- ya da geri alma diye bir şey yoktur — yalnızca "ileri doğru düzeltme" vardır
  ve bozuk bir dağıtım, düzeltme yazılana kadar üretimde kalır.

Namines'te migration'lar açılışta ya da `--migrate` ile koşuyor
(`Program.cs`, `Database:MigrateOnStartup`). Yani ilk seçeneği zorunlu kılan
bir kurala ihtiyaç var.

---

## 2. KURAL: her migration ileriye uyumlu olmalı

> **Bir migration, o migration'dan ÖNCEKİ sürümün kodu tarafından
> çalıştırıldığında da uygulamayı bozmamalı.**

Somut karşılığı: **tek bir dağıtımda hem yeni hem eski şekli aynı anda
desteklenir**, eski şekil ancak *bir sonraki* dağıtımda silinir.

### İzin verilenler (tek adımda güvenli)

| İşlem | Neden güvenli |
|---|---|
| **Nullable** kolon ekle | Eski kod kolonu bilmez, `INSERT`'lerinde yer vermez, veritabanı `NULL` yazar |
| Varsayılanı olan kolon ekle | Aynı — eski kodun `INSERT`'i çalışmaya devam eder |
| Yeni tablo ekle | Eski kod ona hiç dokunmaz |
| Yeni index / unique olmayan kısıt ekle | Okuma-yazma sözleşmesi değişmez |
| Yeni enum değeri **sona** ekle | Sayısal karşılıklar kaymaz (bkz. `GatewayWriteKind.Read` yorumu) |

### YASAK olanlar (tek adımda)

| İşlem | Ne kırar |
|---|---|
| Kolon **sil** | Eski kod hâlâ `SELECT`'liyor → sorgu patlar |
| Kolon **yeniden adlandır** | Hem eski hem yeni kod için yarısı kayıp |
| `NOT NULL` yap (varsayılan olmadan) | Eski kodun o kolonu vermeyen `INSERT`'i reddedilir |
| Tip **daralt** (`text` → `varchar(50)`) | Eski kodun yazdığı uzun değer reddedilir |
| Unique kısıt ekle | Eski kodun yazdığı çift kayıt reddedilir |
| Enum değerini **araya** ekle / sırasını değiştir | Kayıtlı sayısal değerler farklı bir anlama gelir — **veri bozulması**, hata bile vermez |

---

## 3. İki aşamalı desenler

### 3.1 Kolonu yeniden adlandırma

**Tek `RenameColumn` YASAK.** Yerine üç dağıtım:

| Dağıtım | Migration | Kod |
|---|---|---|
| **1** | Yeni kolonu **nullable** ekle | Her iki kolona **yazar**, yeniyi okur, yeni boşsa eskiye düşer |
| **2** | Eski satırlar için yeniyi **doldur** (backfill) | Değişmez |
| **3** | Eski kolonu **sil** | Yalnızca yeniye yazar |

Dağıtım 1 ve 2 arasında geri alma güvenli: eski kod eski kolonu okuyor ve o
kolon hâlâ dolu.

### 3.2 Kolonu `NOT NULL` yapma

| Dağıtım | Migration | Kod |
|---|---|---|
| **1** | — | Kolonu **her zaman** doldurur |
| **2** | Kalan `NULL`'ları doldur + `NOT NULL` yap | Değişmez |

Dağıtım 2'yi 1'in **aynı** sürümünde yapmak, eski kodun `NULL` yazma
ihtimalini açık bırakır — ve geri alma tam da bu ihtimali gerçeğe çevirir.

### 3.3 Kolonu silme

Silme her zaman **ayrı ve sonraki** bir dağıtımda: önce kodu o kolonu hiç
okumayacak hâle getir, dağıt, çalıştığını gör, **sonra** sil. Aksi hâlde geri
alma imkânsızdır — silinen veri geri gelmez.

### 3.4 Tabloyu silme

Aynı kural, artı: silmeden önce **yedek al** (Vault ya da `pg_dump`). Bir
tabloyu yanlışlıkla silmek geri alınamaz.

---

## 4. Geri alma prosedürü

### 4.1 Karar: geri al mı, ileri düzelt mi?

```
Sorun yalnızca KODDA mı? (migration yok ya da §2'ye uygun)
        │
        ├── EVET → GERİ AL (§4.2). Dakikalar sürer.
        │
        └── HAYIR → migration §2'yi ihlal ediyor mu?
                    │
                    ├── HAYIR → GERİ AL. Şema yeni kalır, eski kod çalışır.
                    │
                    └── EVET  → GERİ ALMA YOK. İLERİ DÜZELT (§4.3).
                                Geri almak veriyi bozar.
```

### 4.2 Kod geri alma

```bash
# 1. Hangi imaj çalışıyor, hangisine dönülecek — ÖNCE yaz.
docker compose -f docker-compose.prod.yml ps
```

```bash
# 2. Önceki etikete dön (imaj etiketi elle verilir; "latest" GERİ ALMA DEĞİLDİR).
NAMINES_TAG=<önceki-sürüm> docker compose -f docker-compose.prod.yml up -d --no-deps api
```

```bash
# 3. Hazır olduğunu DOĞRULA — "up" demesi yetmez.
curl -fsS http://localhost:5000/health/ready
```

**`Database:MigrateOnStartup` üretimde `false` olmalı.** Aksi hâlde geri
aldığınız eski sürüm, açılışta migration koşmaya çalışır ve elindeki
migration listesi veritabanındakinden GERİ olduğu için ya hiçbir şey yapmaz
ya da kafa karıştırıcı bir hata verir. Migration ayrı bir adımdır:

```bash
docker compose -f docker-compose.prod.yml run --rm api --migrate
```

### 4.3 İleri düzeltme (geri alınamaz durum)

1. Hatayı kabul et: **üretim bozuk kalacak**, süre düzeltmeyi yazma süresi.
2. Etkiyi sınırla (özelliği kapat, ilgili ucu 503'e al).
3. Düzeltmeyi yaz, testini yaz, dağıt.
4. Olay sonrası: bu migration §2'yi neden ihlal etti, hangi kontrol kaçırdı.

### 4.4 Veritabanını geri alma — son çare

`dotnet ef migrations remove` **uygulanmış** bir migration'ı geri almaz.
Uygulanmış bir migration'ı geri sarmak:

```bash
dotnet ef database update <BirÖncekiMigrationAdı> \
  --project backend/Namines.Infrastructure --startup-project backend/Namines.API
```

**Bu, `Down()` metodunu çalıştırır ve VERİ SİLEBİLİR.** Eklenen kolon
düşürülürken içindeki veri gider. Önce yedek:

```bash
pg_dump "$ConnectionStrings__DefaultConnection" -Fc -f before-rollback.dump
```

Bu adım yalnızca §2'yi ihlal eden bir migration üretime kaçtığında ve
ileri düzeltme mümkün olmadığında düşünülür.

---

## 5. Migration yazarken kontrol listesi

Üretilen dosyayı **okumadan** commit etme (`dotnet ef migrations add` çıktısı
her zaman istenen şeyi yapmaz):

- [ ] Üretilen `Up()`'ta `DropColumn`, `RenameColumn`, `AlterColumn(nullable: false)`
      ya da `AddUniqueConstraint` var mı? Varsa §3'e göre böl.
- [ ] Yeni kolon nullable mı, ya da varsayılanı var mı?
- [ ] Yeni enum değeri **sona** mı eklendi?
- [ ] `Down()` gerçekten `Up()`'ı geri alıyor mu — yoksa boş mu?
- [ ] Uzun süren bir işlem var mı (büyük tabloda index)? Postgres'te
      `CREATE INDEX CONCURRENTLY` gerekir ve EF onu üretmez; elle yazılmalı.
- [ ] Bu migration eski kodla çalıştırılırsa ne olur? Cevap "bilmiyorum" ise
      dağıtma.

---

## 5.1 Bilinçli istisna: Faz 14.2B

`Phase14_2B_DecomposeCloudProjects` bu kuralın dışında (`MigrationCompatibilityTests.DeliberateExceptions`):
`CloudProjects` tek adımda `studio.projects` olur ve 13 kolonu düşer, eski kod yeni şemada ÇALIŞMAZ.

- **Dağıtım:** eski sürümü durdur → yedek al → `--migrate` → yeni sürümü aç. Bakım penceresi gerekir: 50.000 projede
  Decompose 13,7 sn boşta, 18,7 sn okuma yükü altında (denetçi 7-8 sn ölçtü), ReadModel 1,2-1,6 sn; süre boyunca ACCESS EXCLUSIVE.
- **Roller:** `deploy/sql/roles.sql` migrator'a veritabanı `CREATE` verir; önce onu çalıştırın. Rollerle boş kurulum ve Down + Up testli.
- **Geri alma:** kod geri alınacaksa ÖNCE veritabanı: `dotnet ef database update 20261030215219_AddDeletionAuditActions`
  (`Down` verileri geri koyar; önce ReadModel, sonra Decompose), SONRA eski imaj. Düz metin paylaşım token'ı geri gelmez ama linkler özetle çalışır;
  proje başına yalnızca en yeni etkin paylaşım linki döner; boş `studio`/`hub` şemaları durur (varsayılan yetkiler korunsun diye). Ayrıntı: `docs/architecture/PHASE_14_2B_REPORT.md` §3.

## 5.2 Bilinçli istisna: Faz 14.2C

`Phase14_2C_NormalizeCollections` da tek adımlı (`MigrationCompatibilityTests.DeliberateExceptions`): CSV kolonları aynı işlemde tablolara taşınıp düşer.

- **Sıra:** eski sürümü durdur, yedek al, `--migrate`, yeni sürümü aç. Eski kod yeni şemada sorgu hatası verir; maskesiz okuma ya da serbest allowlist oluşmaz.
- **Süre:** ölçüm 200 bin issue + 20 bin anahtar + 50 bin izin için 36-110 sn (makine ve yüke göre), üç tabloda ACCESS EXCLUSIVE; `lock_timeout` 30 sn, açık işlem varsa `55P03` ile düşer, yeniden dene. `Database:MigrationCommandTimeoutSeconds` varsayılanı 1800.
- **Yetki:** `deploy/sql/roles.sql` artık `gateway` şemasını da açıyor; yine de migration `namines_app` varsa yetki verir.
- **Sonrası:** `migration_quarantine.orphaned_rows` içinde `source_table LIKE 'public.Gateway%'` satırlarına bak. Tüm girdileri geçersiz anahtar her isteği reddeder, kullanıcı yenisini üretmeli. `blank_list_means_no_restriction` kısıtsız kalan anahtarları gösterir.
- **Geri alma:** kod geri alınacaksa ÖNCE veritabanı. `dotnet ef` uygulamanın zaman aşımı ayarını okumaz, varsayılan 30 sn hacimde düşer (işlem geri alınır, veri sağlam). Bağlantı dizesinde süre ver:
  `ConnectionStrings__DefaultConnection="<bağlantı>;Command Timeout=1800" dotnet ef database update 20261105105313_AddSubscriptionCancellationOutbox -c AuthDbContext --project backend/Namines.Infrastructure --startup-project backend/Namines.API`
  (200 bin issue'luk DB'de koşuldu: 10,9 sn.) Temiz listeler kanonik döner, karantinadakiler ham metinle; karar aynı kalır. Ayrıntı: `docs/architecture/PHASE_14_2C_REPORT.md` §3.

## 5.3 Bilinçli istisna: Faz 14.2D-1

Tablolar `public`'ten `studio`/`hub`/`gateway`/`ops` altına yerinde taşınır (`ALTER TABLE SET SCHEMA`), uyumluluk görünümü yok. Eski kod yeni şemada sorgu hatası verir (fail-closed).

- **Sıra:** `deploy/sql/roles.sql` (artık `ops` şemasını da açar) → eski sürümü durdur → yedek → `--migrate` → yeni sürümü aç. Tek komut hepsini yapar; Auth önce, sonra Studio, Hub, Gateway, Ops. Kesilirse aynı komut kaldığı yerden devam eder (test: `Interrupted_run_resumes_from_the_failed_module`).
- **Süre (kilit):** her zincir tek işlem, ACCESS EXCLUSIVE taşınan tablolarda; `lock_timeout` 30 sn, açık işlem varsa `55P03` ile düşer ve tekrar denenir. 200 bin issue + 200 bin bağ + 200 bin sürüm + 200 bin denetim: `--migrate` toplam 5 sn, okuma yükü altında en uzun okuyucu beklemesi 2,9 sn (ayrıntı rapor §6).
- **Geri alma:** kod geri alınacaksa ÖNCE veritabanı, modüller TERS sırada, sonra Auth (kolonlar geri gelir, kayıp yok):
  ```
  dotnet ef database update 0 -c OpsDbContext     --project backend/Namines.Ops     --startup-project backend/Namines.API
  dotnet ef database update 0 -c GatewayDbContext --project backend/Namines.Gateway --startup-project backend/Namines.API
  dotnet ef database update 0 -c HubDbContext     --project backend/Namines.Hub     --startup-project backend/Namines.API
  dotnet ef database update 0 -c StudioDbContext  --project backend/Namines.Studio  --startup-project backend/Namines.API
  dotnet ef database update 20261105114438_Phase14_2C_NormalizeCollections -c AuthDbContext --project backend/Namines.Infrastructure --startup-project backend/Namines.API
  ```
  Sıra önemli: Auth'un devir migration'ları boş `Up`/`Down`'dır, tabloları modül `Down`'ı geri getirir. Auth'u önce geri almak veri bozmaz ama eski kod tabloları bulamaz.
- **Hub zinciri iki adım:** `Hub_01_AdoptTables` ve `Hub_02_StatementLevelLabelGc` (etiket temizliği deyim düzeyi). `update 0` ikisini de geri alır; Down satır düzeyi tetikleyiciyi aynı adla geri koyar, 14.2C Down'ı onu düşürebilsin.
- **Roller:** süper kullanıcıyla kurulup sonradan rollere geçen ortamda (`roles.sql` 14.2C'den sonra çalıştırıldıysa) `--migrate`'ten ÖNCE `roles.sql`'i yeniden çalıştırın. Betik artık tabloları, dizileri, fonksiyon ve tetikleyici fonksiyonlarını, tipleri, görünümleri ve şemaların kendisini `namines_migrator`'a devrediyor (`ops` ve `migration_quarantine` dahil). Eski betik yalnız tablo ve diziyi devrediyordu: `hub.issue_label_gc` eski sahibinde kalınca `Hub_02` düşüyordu (denetim R2-M1). Kontrol: `SELECT proowner::regrole FROM pg_proc WHERE oid = 'hub.issue_label_gc()'::regprocedure` sonucu `namines_migrator` olmalı. Unutulursa `Hub_02` "Re-run deploy/sql/roles.sql" diyen 42501 ile durur, işlemi geri alır; betiği çalıştırıp aynı `--migrate` komutunu tekrarlamak yeter (Studio ve Hub_01 uygulanmış kalır, kaldığı yerden devam eder).
- **Sonrası:** Vault/Ground `OrganizationId` kopyaları düşer; `Down` projeden doldurur. `repo_issue_labels` bileşik FK: farklı projeden bağ varsa migration `RAISE` ile durur (`Migration_stops_when_a_cross_project_label_link_exists`), satır karantinaya gitmez, operatör düzeltir.

## 5.2 Abonelik iptali kuyruğu

Silinen hesabın Stripe iptali `PendingSubscriptionCancellations` tablosunda bekler ve işçi tarafından tamamlanır.
`GET /api/admin/subscription-cancellations` içinde `stuck > 0` ya da "iptal edilemedi" ERROR logu görülürse abonelik
Stripe panelinden elle iptal edilir. **Geri alma uyarısı:** bu migration geri alınırsa açık satırlar silinir; önce listeyi kontrol edin.

## 6. Bugün eksik olanlar (dürüst kayıt)

Bu belge kuralı ve prosedürü yazıyor. **Aşağıdakiler hâlâ yok** ve bu belge
onların yerine geçmez:

| Eksik | Sonucu |
|---|---|
| Kuralı zorlayan otomatik kontrol (CI'da migration tarayıcı) | Kural yazılı ama unutulabilir |
| Mavi-yeşil / rolling update tanımı | Dağıtım sırasında kısa kesinti olur |
| Staging ortamı | Migration ilk kez üretimde çalışır |
| Geri alma tatbikatı | Prosedür hiç denenmedi — yazılı olması çalıştığını kanıtlamaz |

Bunlardan ilki (CI kontrolü) en yüksek getirili: kuralın tek zayıf noktası
insanın hatırlaması.

## 5.4 Bilinçli istisna: Faz 14.2D-2

Billing ve Governance tabloları `public`'ten `billing`/`governance` altına yerinde taşınır; `AspNetUsers`'tan beş Stripe kolonu FİZİKSEL düşer (veri `billing.customers`/`billing.subscriptions`'a doğrulanarak kopyalanır). Dağıtım durdur-migrate-aç gerektirir.

- **Sıra:** `deploy/sql/roles.sql` (artık `billing` ve `governance` şemalarını da devreder) → eski sürümü durdur → yedek → `--migrate` → yeni sürümü aç.
- **Ön kontrol (iki yinelenme denetimi):** aynı Stripe müşteri kimliğini ya da aynı Stripe abonelik kimliğini taşıyan iki kullanıcı olmamalı. Karşılaştırma KIRPILMIŞ değerle yapılır, bu yüzden sorgu da kırpar: `SELECT btrim("StripeCustomerId"), count(*) FROM "AspNetUsers" WHERE nullif(btrim("StripeCustomerId"),'') IS NOT NULL GROUP BY 1 HAVING count(*) > 1` ve aynısı `"StripeSubscriptionId"` için. Varsa migration açık hatayla durur (`Billing_01: the same StripeCustomerId|StripeSubscriptionId belongs to more than one user (...)`), `billing.customers` oluşmaz ve hiçbir şey değişmez; yinelenen kayıt elle çözülünce yeniden çalıştırılır. Yeni `billing.subscriptions` filtreli benzersiz bir `StripeSubscriptionId` indeksi taşır (NULL hariç).
- **Kırpma ve boş değer:** taşıma sırasında müşteri ve abonelik kimliklerinin başı/sonu boşluğu kırpılır, boş dizge "yok" sayılır (NULL; müşteri satırı hiç oluşmaz). Down kırpılmış değeri geri koyar: boş → NULL, boşluklu kimlik kırpılmış döner (özgün boşluk bilgisi kaybolur, kimliğin anlamı kaybolmaz).
- **Oturumlar:** kullanıcı satırlarına UPDATE yapılmaz; SecurityStamp, parola özeti ve JWT/yenileme jetonları aynı kalır, oturumlar düşmez.
- **Denetim izi:** `governance."ChangeRequestAuditLogs"` artık UPDATE/DELETE/TRUNCATE kabul etmez (tetikleyici). İstisnalar: kullanıcı silinince `ActorUserId` boşaltma ve saklama süresi temizliği. Temizlik TEK yoldan geçer: tablo sahibi rolün SECURITY DEFINER işlevi `governance.purge_change_request_audit(cutoff, batch_size)`; 90 günden yeni satırı silmez (hata 22023), `EXECUTE` yalnızca sahipte ve `namines_app`'te (PUBLIC'ten geri alındı). Oturum ayarı `namines.audit_retention` tek başına artık DELETE'e izin vermez: tetikleyici ayarın yanında `current_user`'ın tablo sahibi olmasını da ister, bu yüzden uygulama rolü ayarı kendisi açıp silemez. Elle düzeltme gerekirse tablo sahibi tetikleyiciyi geçici kapatır; bunu kayda geçirin.
- **Geri alma (veritabanı önce, modüller TERS sırada):**
  ```
  dotnet ef database update 0 -c DeskDbContext       --project backend/Namines.Desk       --startup-project backend/Namines.API
  dotnet ef database update 0 -c FlowDbContext       --project backend/Namines.Flow       --startup-project backend/Namines.API
  dotnet ef database update 0 -c GroundDbContext     --project backend/Namines.Ground     --startup-project backend/Namines.API
  dotnet ef database update 0 -c VaultDbContext      --project backend/Namines.Vault      --startup-project backend/Namines.API
  (Ops, Gateway, Hub: 5.3'teki gibi)
  dotnet ef database update 0 -c GovernanceDbContext --project backend/Namines.Governance --startup-project backend/Namines.API
  dotnet ef database update 0 -c BillingDbContext    --project backend/Namines.Billing    --startup-project backend/Namines.API
  ```
  Billing Down'ı kolonları geri koyar ve sayı/alan doğrulaması yapar; açık abonelik iptali satırı varsa 2B outbox migration'ının Down'ı `RAISE` ile durur (önce işçi bitirsin ya da elle iptal edilsin).