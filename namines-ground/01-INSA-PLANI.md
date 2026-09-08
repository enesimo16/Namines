# 01 — Namines Ground İnşa Planı

> **Bu dokümanın işi:** [`00-GENEL-BAKIS.md`](00-GENEL-BAKIS.md) *ne* ve *neden*
> sorusunu cevaplıyor; bu doküman **nasıl** sorusunu — iş paketleri, dosya
> dosya yapı, veri modeli, API sözleşmesi ve her adımın kabul kanıtı.
>
> **Durum:** kod yok. Ön koşul: **Vault bitmiş olmalı**
> ([`00-GENEL-BAKIS.md`](00-GENEL-BAKIS.md) §6 — Ground'un "otomatik
> yedekleniyor" vaadi Vault'a dayanıyor, boş bir söz olamaz).
>
> **Biçim:** Vault'un [`01-INSA-PLANI.md`](../namines-vault/01-INSA-PLANI.md)'siyle
> aynı.

---

## Öneri sırası

```
G0  Modül iskeleti + sağlayıcı soyutlaması   — proje, IDatabaseProvider, DI
G1  Neon provizyonu (salt okuma ile kanıt)   — gerçek bir DB oluşur, bağlanılır
G2  Bağlantının projeye bağlanması           — şifreli yazım, Desk'ten görünür
G3  Silme + bekleme penceresi                — soft-delete, geri alma, kalıcı silme
G4  Kota ve kullanım                         — plan sınırı + Neon'dan okunan gerçek sayı
────────────────────────────
G5+ CR'ye bağlı önizleme branch'i, bölge seçimi, ikinci sağlayıcı — G0-G4 kanıtlanmadan başlanmaz
```

**Neden bu sıra:** G1 tek başına bir "yürüyen iskelet" — Neon'da gerçekten bir
veritabanı oluşuyor ve ona bağlanılabiliyor. G2 onu ürüne bağlıyor. G3 **G2'den
hemen sonra** gelmek zorunda: oluşturabilip silemediğin bir kaynak, ilk denemede
çöp biriktirmeye başlar ve faturayı büyütür. G4 en sona kalıyor çünkü kota
sayıları hâlâ açık bir ürün kararı (§8 madde 1).

---

## Ground'un en önemli farkı: bu adım GERÇEK PARA harcıyor

Desk ve Vault'ta yanlış kod kötü bir kullanıcı deneyimi üretiyordu. Ground'da
yanlış kod **fatura** üretiyor ve **veri kaybettiriyor**. Bu yüzden:

- **Her Neon çağrısı, kaynak yaratan bir çağrıdır.** Testler gerçek Neon'a karşı
  koşulmaz; `INeonClient` arayüzü sahte bir uygulamayla test edilir. Gerçek
  doğrulama **elle**, tek seferlik ve kaydedilerek yapılır.
- **Sızdırılan kaynak (orphan) bir hatadır, gürültü değil.** G3'ün sweeper'ı
  isteğe bağlı değil, G1'in tamamlayıcısı.
- **`00-GENEL-BAKIS.md` §2'nin dürüst risk uyarısı** kullanıcıya gösterilen
  metinlerde de korunur; "yönetilen" kelimesi "garantili" anlamına gelmez.

---

## Yapıtaşları — Ground'un SIFIRDAN yazmayacağı şeyler

| İhtiyaç | Var olan parça | Nerede |
|---|---|---|
| Bağlantı dizesini şifrelemek | `IConnectionSecretProtector` | `Namines.Core/Security/`, DI: `ServiceCollectionExtensions.cs:115` |
| Bağlantının saklanacağı alan | `CloudProject.EncryptedConnectionString` + `ConnectionDbType` | `Namines.Core/Models/Auth/CloudProject.cs` |
| Yetki | `OrgAccess.*` | `Namines.Infrastructure/Data/OrgAccess.cs` |
| Arka plan işi (sweeper) | `BackgroundService` + `AddHostedService` | `DockerSweeperBackgroundService.cs` |
| Plan kademesi | `PlanQuotas.Resolve(...)` / `PlanTier` | `Namines.Core/Analysis/` |
| Denetim kaydı deseni | `GatewayAuditEntry` | `Namines.Infrastructure/Data/GatewayAudit.cs` |
| Yedekleme | **Vault** — Ground kendi yedeğini yazmaz | `namines-vault/01-INSA-PLANI.md` |

---

## G0 — Modül iskeleti + sağlayıcı soyutlaması

**Yapılacak:**

```
backend/Namines.Ground/
  Namines.Ground.csproj        → net8.0; ProjectReference: Namines.Core
  Abstractions/
    IDatabaseProvider.cs       → 00-GENEL-BAKIS.md §4.1'deki sözleşme
    ProvisionSpec.cs, ProvisionedDatabase.cs, DatabaseMetrics.cs
    ProviderCapabilities.cs
  Neon/
    INeonClient.cs             → HTTP sınırı; TEST EDİLEBİLİRLİĞİN ANAHTARI
    NeonClient.cs              → gerçek HTTP
    NeonProvider.cs            → IDatabaseProvider uygulaması
  GroundServiceCollectionExtensions.cs
```

`Namines.API/Controllers/GroundController.cs` — rota öneki `/api/ground`.

**`INeonClient` neden ayrı bir arayüz:** `IDatabaseProvider` iş mantığı
(kota, adlandırma, hata çevirme), `INeonClient` ham HTTP. İkisini birleştirmek,
iş mantığını test etmek için gerçek Neon'a çağrı yapmak demekti — yani **her
test çalıştırmasında para harcamak**. Ayrılınca `NeonProvider` sahte bir
istemciyle tamamen test edilebilir hâle geliyor.

**Bağımlılık yönü:** `Namines.API` → `Namines.Ground` → `Namines.Core`.
`Namines.Infrastructure` referansı **yok** (Vault'la aynı kural).

**Kabul kriteri:** Çözüm 0 hata 0 uyarı derlenir; `Namines.Ground.csproj`
`Namines.Infrastructure`'a referans vermez; sahte `INeonClient` ile
`NeonProvider`'ın birim testleri geçer.

---

## G1 — Neon provizyonu

**Yapılacak:**

```
POST /api/ground/provision/{projectId}      (Owner; Admin YETERSİZ)
  1. Yetki: proje Owner'ı — barındırılan bir kaynak açmak en yüksek eşik
  2. Kota: bu plan kaç yönetilen DB alabilir (G4'te gerçek sayı; G1'de
     yapılandırmadan okunan geçici sabit)
  3. İDEMPOTANS: bu proje için zaten Active bir kayıt varsa YENİ KAYNAK
     AÇILMAZ, var olan döner (aşağıya bkz.)
  4. GroundDatabaseRecord: status=Provisioning → DB'ye YAZ (önce kayıt)
  5. NeonProvider.CreateAsync → gerçek Neon projesi/branch'i
  6. status=Active, connection string şifrelenip yazılır (G2)
```

**İdempotans neden G1'in parçası, sonradan eklenecek bir iyileştirme değil:**
Kullanıcı "Barındır" düğmesine iki kez basarsa iki Neon veritabanı oluşur;
ikincisinin kaydı birincinin üzerine yazılır ve birincisi **sonsuza kadar
faturalanan, kimsenin bilmediği bir kaynak** olarak kalır. Bu yüzden:
kayıt **önce** `Provisioning` olarak yazılır ve `(ProjectId)` üzerinde
**unique index** vardır — ikinci istek veritabanı seviyesinde reddedilir.

**Kimlik bilgisi:** `06-DATA-PLANE.md` §3.3'ün deseni — kiracı başına ayrı
kullanıcı (`namines_app_{projectId}`), **`SUPERUSER` değil**.

**Neon API anahtarı** yalnızca sunucuda; hiçbir istemci görmez
(`00-GENEL-BAKIS.md` §5 madde 4).

**Veri modeli:**

```csharp
public class GroundDatabaseRecord {
    public string Id { get; set; }
    public string ProjectId { get; set; }        // UNIQUE index — idempotans
    public string Provider { get; set; }         // "Neon"
    public string ProviderProjectId { get; set; }
    public string ProviderBranchId { get; set; }
    public string Region { get; set; }
    public string Status { get; set; }           // Provisioning|Active|PendingDelete|Deleted|Failed
    public string CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeleteRequestedAt { get; set; }  // G3
    public DateTime? DeletedAt { get; set; }
    public string? Error { get; set; }
}
```

**Kabul kriteri (kanıt = çalışan komut + görülen çıktı):**
1. Provizyon çağrılır; **Neon panelinde** veritabanının gerçekten oluştuğu
   görülür.
2. Dönen bağlantıyla **bağımsız bir `psql` oturumu** açılır, `SELECT 1`
   çalışır — API'nin "başarılı" demesi yeterli sayılmaz
   (`00-GENEL-BAKIS.md` §9 madde 2).
3. Aynı uca ikinci kez çağrı yapılır → **yeni kaynak oluşmaz**, var olan döner.
4. Neon API'si hata döndüğünde `status=Failed`, `Error` dolu ve **yarım kalmış
   bir Neon kaynağı kalmaz** (oluşup sonra hata alındıysa temizlenir).

---

## G2 — Bağlantının projeye bağlanması

**Yapılacak:** G1'in ürettiği bağlantı dizesi, Desk/Vault'un kullandığı **aynı**
`IConnectionSecretProtector` ile şifrelenip `CloudProject.EncryptedConnectionString`
alanına yazılır; `ConnectionDbType = "PostgreSQL"`.

**Bunun bedava getirdiği şey:** Desk'in Veri/Şema/SQL ekranları, Gateway'in
CRUD'u ve Vault'un yedeği o an **çalışır hâle gelir** — hepsi zaten bu alanı
okuyor. Ground yeni bir veri yolu açmıyor, var olanı besliyor.

**Üzerine yazma koruması:** Projede zaten kullanıcının kendi girdiği (BYODB) bir
bağlantı varsa, Ground onu **sessizce ezmez** — açık onay ister. Sessizce ezmek,
kullanıcının prod veritabanına giden yolu boş bir Ground veritabanına çevirmek
demekti.

**Kabul kriteri:** Provizyondan sonra Desk'te aynı proje açılır; Şema ekranı
boş bir veritabanı gösterir, SQL konsolu `SELECT 1` çalıştırır — **hiçbir yerde
bağlantı dizesi tarayıcıya gitmeden**.

---

## G3 — Silme + bekleme penceresi

> G2'den hemen sonra gelmek **zorunda**: silinemeyen bir kaynak, fatura büyüten
> bir sızıntıdır.

**Yapılacak:**

```
DELETE /api/ground/{projectId}     (Owner; body: { confirmProjectName })
  1. confirmProjectName TAM eşleşmeli
  2. status=PendingDelete, DeleteRequestedAt=now  → Neon'da HENÜZ SİLİNMEZ
  3. Bekleme penceresi (açık karar §8 madde 2; öneri 7 gün) boyunca
     "Son silinenler" listesinde görünür ve GERİ ALINABİLİR (§10 madde 4)
  4. GroundSweeperService (BackgroundService): penceresi dolanları
     NeonProvider.DeleteAsync ile kalıcı siler, status=Deleted
```

**Ayrıca — orphan sweeper (aynı serviste):** Neon'da var olup Namines'te
kaydı olmayan kaynaklar raporlanır. Bunlar `Provisioning` sırasında çöken
isteklerin kalıntısıdır ve **otomatik silinmez, loglanır** — otomatik silme,
bir kayıt okuma hatasında kullanıcının canlı veritabanını silmek demekti.

**Kabul kriteri:**
1. Silme istenir → Neon'da veritabanı **hâlâ durur**, listede "silinecek"
   görünür.
2. Geri alınır → status `Active`'e döner, bağlantı çalışmaya devam eder.
3. Pencere zorlanarak doldurulur → Neon panelinde kaynağın **gerçekten
   silindiği** görülür.
4. Yanlış proje adında işlem başlamaz.

---

## G4 — Kota ve kullanım

**Yapılacak:**
- Provizyon öncesi plan bazlı sınır kontrolü (`PlanQuotas` deseni).
- Depolama/compute kullanımı **Neon'un API'sinden okunur**, Namines kendi
  saymaz (`00-GENEL-BAKIS.md` §9 madde 4: uydurma sayı yok).
- Desk'in Projeler panosunda gösterim (§10 madde 2).

**Kabul kriteri:** Kotası dolu bir hesapta provizyon **reddedilir** ve mesaj
hangi sınıra takıldığını söyler; gösterilen kullanım rakamı Neon panelindeki
rakamla **eşleşir**.

---

## Test stratejisi

| Katman | Nasıl | Not |
|---|---|---|
| `NeonProvider` iş mantığı | Sahte `INeonClient` ile xUnit | **Gerçek Neon'a çağrı YOK** — her test para harcardı |
| İdempotans / unique index | Testcontainers PostgreSQL | `RequiresDockerFact` |
| Silme penceresi / sweeper | Saat enjekte edilerek birim testi | Gerçek bekleme yok |
| Gerçek provizyon | **Elle**, tek seferlik, kaydedilerek | G1'in kabul kriteri |

---

## Riskler ve azaltma

| Risk | Neden ciddi | Azaltma |
|---|---|---|
| Çift provizyon | Faturalanan hayalet kaynak | `(ProjectId)` unique index + önce-kayıt (G1) |
| Yanlış silme | Geri alınamaz veri kaybı | Proje adı yazma + bekleme penceresi (G3) |
| BYODB bağlantısının ezilmesi | Kullanıcı prod DB'sine erişimini kaybeder | Açık onay (G2) |
| Neon kesintisi | Kullanıcının veritabanı erişilemez | Sağlık göstergesi (§10 madde 3); SLA Neon'un — bu **dürüstçe** söylenir |
| Neon API anahtarı sızması | Tüm kiracı veritabanları risk altında | Yalnızca sunucu tarafı, loglanmaz, Ground vekil davranır |
| Kota okunamadığında ne olur | Sınırsız provizyon | Okunamıyorsa **reddet** — belirsizlikte kaynak açmak yanlış yön |

---

## Açık kararlar (kod başlamadan netleşmeli)

`00-GENEL-BAKIS.md` §8'in üç maddesi (kotalar, silme bekleme süresi, Neon
organizasyon yapısı) + bu planın açtığı ikisi:

4. **Neon faturalama sahipliği** — kaynaklar Namines'in Neon hesabında mı
   toplanacak (Namines öder, kullanıcıya yansıtır), yoksa kullanıcının kendi
   Neon hesabına mı bağlanacak? Bu, ürünün maliyet modelini doğrudan belirler
   ve G1'den önce netleşmeli.
5. **Provizyon süresi ve arayüz davranışı** — Neon provizyonu saniyeler
   sürüyor; senkron mu beklenecek yoksa Vault gibi asenkron mu? (Öneri:
   senkron, ama 10 sn üstünde asenkrona düş.)

---

## Kalıcı olarak Ground'a GİRMEYECEKLER

`00-GENEL-BAKIS.md` §7 aynen geçerli. Bu planın eklediği iki madde:

- **Kullanıcının Neon panelini Namines içinden yönetmesi.** Ground bir vekil,
  bir Neon arayüzü klonu değil; Neon'un her özelliğini aynalamak, sağlayıcıya
  bağımlılığı gizlemek yerine iki katına çıkarır.
- **Otomatik ölçekleme kararları.** Boyut sınıfını kullanıcı seçer; Namines
  "senin adına büyüttüm" diyerek fatura üretmez.
