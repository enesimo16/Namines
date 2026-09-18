# 01 — Depo Tarama: GitHub linki → şema

> **Sıra: 1 (ilk teslim edilecek dilim).** GitHub App gerektirmez.
> Yeni ayrıştırıcı yazılmaz — var olan zincire yalnızca **dosya kaynağı** eklenir.

## Ne

Kullanıcı bir depo linki yapıştırır (`https://github.com/acme/shop`), Namines
depoyu tarar, içindeki model/migration tanımlarından şemayı çıkarır ve canvas'a
koyar — ya da canvas'takiyle karşılaştırıp farkı gösterir.

Bugünkü akışta aynı iş yapılabiliyor ama **dosyaları kullanıcı tek tek
yüklüyor.** Tek fark bu: kaynak, dosya seçici yerine depo.

## Neden bu ilk

- `PrismaSchemaParser` + `EfCoreEntityParser` + `SqlDdlSchemaParser` +
  `SchemaUuidAligner` + `compareWith` zinciri **tamamen hazır ve doğrulanmış**
  (bkz. `second-phase/11-KODDAN-SEMA.md`).
- Public depo için kimlik gerekmez → GitHub App'i beklemez.
- Tek cümlede anlaşılır ve gösterilebilir: *"linki yapıştır, şemanı gör."*

---

## Nasıl

### 1. İstemci yüzeyini genişlet

`IGithubClient` bugün tek dosya okuyor. Eklenecek üç metot:

| Metot | Neden |
|---|---|
| `GetRepositoryTreeAsync(repo, reference)` | `GET /repos/{o}/{r}/git/trees/{ref}?recursive=1` — **tek çağrıda** bütün dosya listesi. Dizin dizin gezmek yüzlerce istek demek olurdu. |
| `GetFileContentsAsync(repo, paths, reference)` | Yalnızca **aday** dosyaların içeriği. Blob başına bir istek; bütçe bunun için var. |
| `GetBranchesAsync(repo)` | Kullanıcının hangi branch'ten içe aktaracağını seçmesi. |

**Kimliksiz mod:** `IGithubClient` bugün installation token'a bağlı. Public
depo için token'sız bir yol gerekiyor — aynı arayüz, `installationId = null`
ise `Authorization` başlığı gönderilmez. Rate limit (saatte 60) kullanıcıya
**dürüstçe** bildirilir, sessizce başarısız olunmaz.

### 2. `RepositoryScanner`

Tree'den aday dosyaları **öncelik sırasıyla** seçer:

| Öncelik | Desen | Ayrıştırıcı |
|---|---|---|
| 1 | `.namines/schema.nsl`, `ir.json` | Namines'in kendi IR'ı — varsa tarama bile gerekmez |
| 2 | `**/*.prisma` | `PrismaSchemaParser` |
| 3 | `supabase/migrations/*.sql`, `**/migrations/*.sql` | `SqlDdlSchemaParser` |
| 4 | `**/*DbContext.cs`, `**/Entities/*.cs`, `**/Models/*.cs` | `EfCoreEntityParser` |
| 5 | kökteki `schema.sql`, `init.sql`, `**/*.sql` | `SqlDdlSchemaParser` |

**Var olan bütçe aynen korunur: 200 dosya / 2 MB.** Bütçeyi aşanlar atılmaz,
"atlandı + neden" olarak bildirilir. Yüksek öncelikli dosyalar önce alınır —
bütçe dolarsa kaybedilen `node_modules` altındaki bir `.sql` olur, `schema.prisma`
değil.

**Gürültü filtresi (tarama öncesi):** `node_modules/`, `.next/`, `bin/`, `obj/`,
`vendor/`, `dist/`, `test/fixtures/` yolları hiç aday sayılmaz. Bu filtre
olmadan 200 dosyalık bütçe bir Next.js deposunda ilk 200 `node_modules`
dosyasıyla dolar.

### 3. Uç

```
POST /api/github/scan
  { repoUrl, branch?, compareWith? }
→ { detectedFormat, parsedCount, skippedCount,
    files: [{ path, parser, status, skipReason? }],
    schema: DatabaseSchema,
    impact?: ImpactReport }        // compareWith verildiyse
```

`schema` üretimi ve `compareWith` karşılaştırması **var olan
`CodeSchemaExtractor` ve `SchemaImpactAnalyzer`'a** devredilir; bu uç yalnızca
dosyaları toplar. `SchemaUuidAligner` adımı da aynen geçerli — koddan çıkan
şemada UUID yok, hizalama olmadan rapor her tabloyu "silindi + eklendi" gösterir
(bu hata bir kez yaşandı ve testle kilitlendi, bkz. `second-phase/11`).

### 4. UI

Canvas'taki "Schema from Code" paneline ikinci sekme: **"From GitHub repo"**.

```
[ https://github.com/acme/shop        ]  [ Tara ]
Branch: ( main ▾ )

Bulunanlar (3)                      Atlananlar (12)
  prisma/schema.prisma   Prisma       node_modules/...  filtrelendi
  supabase/migrations/…  SQL DDL      docs/legacy.sql   2 MB bütçesi doldu
  src/Models/User.cs     EF Core      infra/*.tf        tanınmayan format

[ Canvas'a aktar ]   [ Canvas'la karşılaştır ]
```

**Atlananlar listesi kapatılamaz.** Kullanıcının "her şeyi gördü" sanması,
eksik şemayla ilerlemesinin tek sebebi olur.

---

## ⚠️ Dikkat

- **Private depo bu dilimde yok.** Link private ise net bir mesajla "GitHub
  App kurulumu gerekiyor" denir ve [02](02-BOT-VE-PREVIEW-DB.md)'ye yönlendirilir.
  Yarım bir kimlik yolu uydurulmaz.
- **SSRF sınırı.** `repoUrl` yalnızca `github.com` host'unu kabul eder;
  `DbIntrospectController`'daki aynı güvenlik modeli (bkz. AGENTS.md).
- **Rate limit dürüstçe.** 403/429 geldiğinde "tarayamadım" değil, "GitHub
  saatlik limiti doldu, X dakika sonra veya bir kurulumla tekrar dene".
- **Büyük tree.** `truncated: true` dönen depolarda tree eksiktir; bu da
  "atlandı" olarak raporlanır.

## 🔴 Yapılmayacak

- **Depoyu klonlamak.** Yalnızca API üzerinden okuma. Disk alanı bu makinede
  zaten kritik (AGENTS.md) ve klonlamak `.git` geçmişini de indirmek demek.
- **Tanınmayan formatı AI'ya sormak.** 11 numaranın kuralı: ikisini gerçekten
  iyi yapmak, sekizini yarım yapmaktan değerli.
- **Migration dosyalarını çalıştırmak.** Ham metin ayrıştırılır, SQL
  çalıştırılmaz, hiçbir veritabanına bağlanılmaz.
- **Depoya yazmak.** Bu dilim salt-okunur. Yazma [04](04-YAZMA-YOLU-VE-IZIN.md).

## Doğrulama (bu iş "bitti" demeden önce)

Testler yeşil olmak **hiçbir şey kanıtlamıyor** (AGENTS.md'nin en pahalı dersi).
Bitti demeden önce gerçek depolara karşı çalıştır:

1. Bir Prisma deposu (public) → şema canvas'ta doğru mu.
2. Bir .NET deposu → `DbContext` + `Migrations/` birlikte varken hangisi seçildi.
3. Bir Next.js monorepo → gürültü filtresi olmadan ve olarak, aday sayısı farkı.
4. `supabase/migrations/` klasörü → `auth.*` şemaları dışlandı mı (11'de
   doğrulanan davranış korunuyor mu).
5. Var olmayan depo / private depo / rate limit → üç farklı, dürüst mesaj.
