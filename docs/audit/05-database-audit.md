# 05 — Veritabanı denetimi

Bu bir veritabanı ürünü olduğu için bölüm ikiye ayrılıyor:
**(A)** Namines'in KENDİ control DB'si, **(B)** kullanıcının veritabanına
dokunan kod yolları.

---

# A. Control DB (Namines'in kendi verisi)

**Motor:** PostgreSQL 17 (`docker-compose.yml:4`). SQLite'tan geçiş tamamlanmış.
**Erişim:** EF Core 8, `AuthDbContext` (372 satır, 34 `DbSet`).
**Migration:** açılışta otomatik uygulanıyor.

## DB-001 — İndeks kapsamı: SAĞLIKLI

24 adet `HasIndex` tanımı var ve **doğru yerlerde**:

| İndeks | Neden doğru |
|---|---|
| `(BranchId, Version)` | Sürüm arama; branch başına sıralı erişim |
| `(ProjectId, CreatedAt)` | Zaman sıralı listeleme — sayfalama için gerekli |
| `(ChangeRequestId, UserId)` | Oy tekilliği; unique olarak tanımlı |
| `(UserId, BillingPeriod, Resource)` | Kota toplaması; N+1'i engelliyor |
| `GatewayApiKey.Prefix` | Anahtar arama, tam anahtar olmadan |
| `TeamInvite.TokenHash`, `DeskHandoffToken.TokenHash` | Jeton doğrulama; **hash indeksli, jetonun kendisi saklanmıyor** |
| `(ProjectId, TableName)` | Tablo izni araması |

Bileşik indekslerde kolon sırası sorgu desenine uygun. **Bulgu yok.**

## DB-002 — Cascade davranışı

`AGENTS.md` bir `ON DELETE CASCADE` hatasının geçmişte bulunup düzeltildiğini
ve golden-file testleriyle korunduğunu kaydediyor. Golden testler
`backend/Namines.Tests/Golden/` altında 6 motor × fixture olarak duruyor.

**Bulgu yok** — ama bu, denetimin **kod okumasıyla** doğruladığı bir alan;
cascade davranışı canlı olarak yeniden denenmedi.

## DB-003 — Migration'ların açılışta otomatik uygulanması

### Problem
Tek instance'ta sorunsuz. **Çoklu instance**'ta iki API aynı anda açılırsa
ikisi de `Migrate()` çağırır; EF'in kilidi çoğu durumda korur ama uzun bir
migration sırasında ikinci instance zaman aşımına düşebilir.

Daha önemlisi: geri alınamaz bir migration, **deploy anında sessizce**
uygulanıyor — ayrı bir onay adımı yok. Ürünün kendisi tam da bunun için
ChangeRequest akışı sunarken kendi şemasına aynı disiplini uygulamıyor.

### Severity
**MEDIUM**

### Recommendation
Migration'ı uygulamadan **ayır**: ayrı bir job/adım (`dotnet ef database update`
ya da tek seferlik bir init container). Uygulama açılışta yalnızca "beklenen
migration uygulanmış mı" diye **kontrol edip** değilse fail-fast versin.

### Effort M · ### Priority P2

---

# B. Kullanıcının veritabanına dokunan yollar

Ürünün riskli yüzeyi burası. Üç ayrı yol var:

| Yol | Ne yapıyor | Yazma? | Denetim kaydı |
|---|---|---|---|
| `DbIntrospectController` | Şema okur | Hayır | **Yok** |
| `GatewayController` | CRUD + rpc + query | Evet | **Var** (`GatewayAuditEntry`) |
| `DatabaseExecutorController` | Keyfi SQL betiği | Evet | **Yok** |

## DB-004 — Dinamik SQL: tek savunma hattı, ama tutarlı uygulanmış

Kullanıcı verisinin SQL'e girdiği tek yer `GatewayService`. Orada:

- `ValidateIdentifierOrThrow` + `^[A-Za-z_][A-Za-z0-9_]*$` — **20+ çağrı
  noktasında tutarlı**: tablo, kolon, sıralama kolonu, fonksiyon adı, expand
  hedefi, alias, projeksiyon kolonları.
- Değerler **parametre** olarak bağlanıyor (`@skip`, `@take`, `@pkvalue`).
- Sıralama yönü kullanıcı metninden değil **enum**'dan yazılıyor
  (`GatewayService.cs` — "Yön SQL'e ENUM'dan yazılır" yorumu).
- Testleri var: `GatewayServiceTests.cs:152` `"Id; DROP TABLE Users--"` reddini
  doğruluyor.

Bu, denetimin en çok aradığı ve **bulamadığı** açık. Kayda geçiyor.

**Tek uyarı** (bkz. SEC "derinlemesine not"): `Quote()` sınırlayıcıyı kaçırmıyor,
yani güvenlik tamamen o tek regex'e bağlı. Derinlemesine savunma önerildi.

## DB-005 — Sayfalama: motor başına doğru, ama kararlılık uyarısı UI'a bırakılmış

`BuildListSql` her motorun kendi sözdizimini üretiyor (MSSQL/Oracle
`OFFSET…FETCH`, MySQL `LIMIT skip, take`, Postgres `LIMIT/OFFSET`).

Kod, `ORDER BY` olmadan sayfalamanın **kararsız** olduğunu biliyor ve yorumda
açıklıyor: sayfa 2'de aynı satır tekrar gelebilir, başkası hiç gelmeyebilir.
Sıralama kolonu verilmediğinde eski davranış korunuyor ve "çağıran (UI)
kullanıcıyı uyarır" deniyor.

### Problem
Bu, doğruluk garantisini **UI'ın hatırlamasına** bırakıyor. `services/desk` bunu
gösteriyor mu, `frontend` gösteriyor mu — sözleşmede yazılı değil.

### Severity **LOW** · ### Recommendation
Yanıta `stablePagination: false` gibi bir alan koy; UI'ın onu göstermesi
zorunlu olmasa da en azından sözleşmede görünür olsun.
### Effort S · ### Priority P3

## DB-006 — Bağlantı zaman aşımı ve sorgu zaman aşımı: TANIMLI

`GatewayService.cs:46-47`:
```csharp
private const int ConnectTimeoutSeconds = 10;
private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(15);
```

Kullanıcının yavaş veritabanı API thread'lerini süresiz tutamıyor. **Bulgu yok.**

`DatabaseExecutorService`'te karşılığı **görülmedi** — keyfi betik çalıştıran
yolda zaman aşımı yoksa uzun bir DDL bağlantıyı ve isteği bloke eder.
**Öneri (P2, S):** executor'a da açık `CommandTimeout` koy.

## DB-007 — İşlem (transaction) yönetimi

`ExecuteScriptAsync` betiği **tek transaction** içinde çalıştırıyor, hata olursa
geri alıyor ve kaçıncı ifadede patladığını söylüyor. Bu doğru.

**Uyarı:** PostgreSQL dışındaki motorlarda DDL çoğu zaman **implicit commit**
yapar (MySQL/MariaDB'de `CREATE TABLE` transaction'ı otomatik commit'ler;
Oracle da öyle). Yani MySQL'de "hepsi ya da hiçbiri" garantisi **yok**, ama kod
ve arayüz onu veriyor gibi davranıyor.

### Severity **MEDIUM** · ### Location `DatabaseExecutorService.ExecuteScriptAsync`
### Recommendation
Motor DDL'de transactional değilse kullanıcıya **açıkça söyle**: "MySQL'de kısmi
uygulama mümkündür; başarısızlıkta N ifade uygulanmış olabilir." Sessiz yanlış
garanti, garantisizlikten kötüdür.
### Effort S · ### Priority P1

---

## Desteklenen motorlar — gerçek durum

| Motor | Şema okuma | DDL üretimi | Gateway CRUD | Vault yedek |
|---|---|---|---|---|
| PostgreSQL | ✅ | ✅ | ✅ | ✅ canlı doğrulandı |
| MySQL | ✅ | ✅ | ✅ | ✅ canlı doğrulandı |
| MariaDB | ✅ | ✅ | ✅ | ✅ canlı doğrulandı |
| MSSQL | ✅ | ✅ | ✅ | ❌ mimari sınır |
| Oracle | ✅ | ✅ | ✅ | ❌ mimari sınır |
| SQLite | ✅ | ✅ | ✅ | ❌ dosya tabanlı |

Vault'ta MSSQL/Oracle'ın dışarıda kalması bir eksiklik değil **mimari sınır**:
`BACKUP DATABASE TO DISK` ve `expdp` dosyayı sunucunun kendi diskine yazar,
istemciye akıtılabilen çıktı vermezler. Gerekçe
`namines-vault/02-ERISIM-VE-DEPLOY.md` içinde belgelenmiş.

**Not:** MSSQL ve Oracle'ın ilişki (FK) sorguları
`DURUM.md`'de **doğrulanmamış** olarak işaretli — MySQL canlı doğrulandı,
diğer ikisi yazıldı ama gerçek sunucuya karşı çalıştırılmadı. Bu, ürünün
"6 motor destekliyor" iddiasının en zayıf noktası.
