# 02 — Mimari denetimi

## Gerçek mimari (ölçüldü, varsayılmadı)

```
Tarayıcı
   │
   ├── frontend/          Next.js 16 · React 19 · App Router · ~32k satır
   │      │               Rotalar: / /canvas /compile /demo /new /review
   │      │                        /share/[token] /join/[token] /privacy /terms /security
   │      │
   │      └── axios + fetch ──┐
   │                          │
   └── services/desk/     Next.js · AYRI mikroservis · ~6k satır
          │                   Ana backend'e YALNIZCA HTTP ile bağlanır
          └── fetch ──────────┤
                              ▼
                    backend/Namines.API          43 dosya · 10.781 satır
                    33 controller · JWT (httpOnly cookie fallback) · rate limiter
                              │
                              ▼
                    backend/Namines.Infrastructure  96 dosya · 20.388 satır
                    Servisler · EF Core · AI sağlayıcıları · üreteçler
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
        Namines.Core    Namines.Vault    Namines.Ground
        139 dosya       12 dosya         6 dosya
        11.074 satır    1.432 satır      933 satır
        (model + arayüz) (yedekleme)     (yönetilen DB)
              │
              ▼
        PostgreSQL (control DB) — EF migration'ları açılışta uygulanıyor
```

Ek çıktılar: `Namines.Mcp` (MCP sunucusu), `Namines.Cli`.

---

## ARCH-001 — Modül sınırı derleyiciyle korunuyor (GÜÇLÜ YAN)

`Namines.Vault` ve `Namines.Ground` **yalnızca** `Namines.Core`'a referans veriyor;
`Namines.Infrastructure`'ı hiç görmüyorlar (`Namines.Vault.csproj`,
`Namines.Ground.csproj`). Bağımlılık ters yönde: Infrastructure onları görüyor.

Bu, "katmanlı mimari" iddiasının **belge değil, derleyici** tarafından
zorlandığı ender bir örnek. Bir geliştirici Vault içinden `AuthDbContext`'e
uzanmaya kalkarsa derleme kırılır. Bu korunmalı.

**Karar: KEEP.** Değişiklik önerilmiyor.

---

## ARCH-002 — `GatewayController` 1.447 satır, 15 uç

### Location
`backend/Namines.API/Controllers/GatewayController.cs`

### Current State
Deponun en büyük controller'ı; ikinci sıradakinin (`SchemaController`, 662)
iki katından fazla. Sınıf düzeyinde `[AllowAnonymous]` (satır 114) ve kimlik
API anahtarından çözülüyor (satır 369 civarındaki yorum bunu açıklıyor).

### Problem
Tek bir dosya şunların hepsini taşıyor: anahtar doğrulama, tablo izni kontrolü,
liste/detay/yazma/silme/import/rpc/query/query-nl, OpenAPI üretimi, denetim
kaydı. Yeni bir uç eklemek her seferinde bu dosyaya dokunmayı gerektiriyor;
birleştirme çakışması ve gözden kaçan yetki kontrolü riski buradan geliyor.

### Risk
Yetki kontrolü **her uçta elle tekrarlanıyor**. Bir uç eklenirken unutulursa
derleyici uyarmaz — ve `[AllowAnonymous]` sınıf düzeyinde olduğu için varsayılan
**açık** tarafa düşer.

### Severity
**MEDIUM** (bugün bir açık bulunamadı; yapı gelecekteki açığı davet ediyor)

### Recommendation
Anahtar çözme + izin kontrolünü bir **filtre/middleware**'e taşı
(`[GatewayKeyRequired]` gibi). Böylece varsayılan **kapalı** olur: filtreyi
eklemeyi unutan uç 401 döner, sessizce açılmaz. Ardından controller'ı okuma /
yazma / meta olarak üçe böl.

### Effort
L

### Priority
P1

---

## ARCH-003 — `ScaffolderService` 1.760 satır

### Location
`backend/Namines.Infrastructure/Services/ScaffolderService.cs`

### Problem
Deponun en büyük tek dosyası. Üreteç kodu doğası gereği uzundur (şablon
metni), ama 1.760 satır tek sorumluluk olamaz.

### Recommendation
Hedef dil/çatı başına ayrı üreteçlere böl — `Generators/Eject/` altında bu
desen **zaten var** (`ConsoleNextjsGenerator`, `TypeScriptSdkGenerator`,
`PythonGenerators`). Yani çözüm icat edilmeyecek, mevcut desen uygulanacak.

### Severity
**LOW** (bakım yükü; davranışsal risk yok)
### ✅ Yapıldı (11.09.2026) — B-37

`ScaffolderService` **1.761 → 120 satır**. Artık yalnızca orkestrasyon
(hangi dosya hangi yola); içerikler `Generators/Scaffold/` altında hedef
başına beş statik üreteçte:

| Üreteç | Satır | İçerik |
|---|---|---|
| `DotnetBackendScaffold` | 408 | entity, DbContext, controller, Program.cs, csproj, Docker |
| `FrontendSdkScaffold` | 188 | TS tipleri, Zod, TanStack Query kancaları |
| `CloudInfraScaffold` | 294 | AWS / Azure Terraform + GitHub Actions |
| `BiModuleScaffold` | 390 | Text-to-SQL denetleyicisi + React bileşeni |
| `PythonScaffold` | 464 | FastAPI + SQLAlchemy projesi |

**Önce bir güvenlik ağı kuruldu, çünkü yoktu.** Servisin çıktısını kapsayan
TEK bir test bulunmuyordu. Bölmeden ÖNCE `ScaffolderSnapshotTests` yazıldı:
4 varyant (.NET düz, .NET+BI+AWS, .NET+Azure, Python), zip'in içindeki her
dosyanın yolu ve içeriği. Zip baytları değil içerik karşılaştırılıyor —
başlıklardaki zaman damgaları her üretimde değişir.

Sonuç: bölmeden sonra 4 snapshot da **birebir aynı**. Metot gövdeleri
değişmedi; yalnızca `private` → `internal static` ve çağrılar sınıf adıyla
nitelendi. Yardımcılar gruplar arasında çağrılmıyordu (ölçüldü), bu yüzden
bölme temiz.

**Testin kendisi doğrulandı:** bir üreteçte tek bir kelime değiştirildiğinde
snapshot testi **başarısız** oluyor, geri alınınca geçiyor. Yani test
gerçekten çıktıyı ölçüyor — sessizce geçen bir güvenlik ağı değil.


### Effort
M · ### Priority P2

---

## ARCH-004 — DTO / entity sınırı

### Bulgu
`AuthController.cs:355` gibi noktalarda yanıtlar `new { ... }` anonim
nesnelerle **elle projekte ediliyor**; entity doğrudan dönmüyor.

Bu iyi bir sonuç ama **yapısal bir garanti değil**: her uç bunu kendi
hatırlamak zorunda. Bir `return Ok(project)` bir gün `EncryptedConnectionString`
alanını sızdırabilir.

### Recommendation
Hassas alan taşıyan entity'ler (`CloudProject.EncryptedConnectionString`,
`GatewayApiKey` hash'leri) için `[JsonIgnore]` ekle. Böylece kaza donanımsal
olarak engellenir, dikkate bağlı kalmaz.

### Severity
**MEDIUM** · ### Effort S · ### Priority P1

---

## Değerlendirme tablosu

| Soru | Cevap | Kanıt |
|---|---|---|
| Katmanlar doğru ayrılmış mı? | **Evet** | csproj referans grafiği; Vault/Ground → yalnızca Core |
| Separation of Concerns | Büyük ölçüde | İki dev dosya istisna (ARCH-002, ARCH-003) |
| DI doğru mu? | Evet | Program.cs'de lifetime seçimleri gerekçeli; `DbContext` scoped |
| SOLID | Çoğunlukla | `IBackupProvider` / `IDatabaseProvider` açık-kapalı ilkesinin iyi uygulaması |
| Clean Architecture gerekli mi? | **Hayır** | Mevcut 4 katman yeterli; ek soyutlama katmanı maliyeti karşılamaz |
| Gereğinden karmaşık mı? | Hayır | Aksine, bazı yerlerde daha fazla ayrıştırma gerekli |
| Repository katmanı gerekli mi? | **Hayır** | EF Core zaten repository+UoW; ikinci sarmalayıcı katma değer üretmez |
| Circular dependency | Yok | Referans grafiği asiklik |
| Business logic nerede? | Infrastructure/Services | Controller'lar ince (ARCH-002 hariç) |

---

## Kararlı olarak ÖNERİLMEYEN değişiklikler

Bunlar "best practice" listelerinde geçer ama bu projede **yanlış** olur:

1. **Repository pattern eklemek** — EF Core'un `DbSet`'i zaten repository.
   Üzerine katman koymak test edilebilirliği artırmaz, dosya sayısını artırır.
2. **MediatR / CQRS** — bu ölçekte tören. Mevcut controller → service akışı
   okunabilir ve izlenebilir.
3. **AutoMapper** — elle projeksiyon, tam da ARCH-004'te değindiğim sızıntıyı
   görünür kılıyor. AutoMapper onu gizlerdi.
