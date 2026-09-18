# GitHub Platformu — Genel Bakış

> **Bu klasör ne:** Namines'in GitHub'la ilişkisinin bütün planı. Bugün var
> olanın envanteri, eksik olan tek kavram, ve sırasıyla ne yapılacağı.
>
> **Bu klasör ne DEĞİL:** yeni bir ürün. Buradaki işlerin çoğu var olan
> parçalara **kaynak** ve **tetikleyici** eklemek. Yeni ayrıştırıcı, yeni
> analizör, yeni diff motoru yazılmıyor — hepsi zaten var.

İlgili mevcut dokümanlar: [`new-phase/11-MIGRATIONS-BRANCHING.md`](../new-phase/11-MIGRATIONS-BRANCHING.md)
§7 (Bot) ve §9 (CLI/CI), [`second-phase/11-KODDAN-SEMA.md`](../second-phase/11-KODDAN-SEMA.md),
[`second-phase/12-ENTEGRASYONLAR.md`](../second-phase/12-ENTEGRASYONLAR.md),
[`new-phase/34-SENDEN-BEKLENENLER.md`](../new-phase/34-SENDEN-BEKLENENLER.md) §8.

---

## 1. Bugün elimizde olan (yeniden keşfetme)

| Parça | Nerede | Durum |
|---|---|---|
| GitHub App kimliği | `Namines.Core/Github/GithubAppJwt.cs` | ✅ kod hazır, kimlik bilgisi yok |
| Webhook + HMAC doğrulama | `Namines.API/Controllers/GithubWebhookController.cs` | ✅ |
| Yazma istemcisi | `IGithubClient` → yorum, status check, **tek dosya okuma** | ✅ dar ama çalışıyor |
| Bot | `GithubBotService` — PR'daki `.nsl` base↔head farkı → `SchemaImpactAnalyzer` → yorum + `namines/schema-review` | ✅ |
| `/namines` komutları | Ayrıştırıcı var | 🟡 "henüz yok" cevabı veriyor |
| Koddan şema | `PrismaSchemaParser`, `EfCoreEntityParser`, `SqlDdlSchemaParser`, `CodeSchemaExtractor`, `SchemaUuidAligner` | ✅ ama dosyayı kullanıcı **elle yüklüyor** |
| Drift karşılaştırması | `POST /api/codeschema/extract` + `compareWith` | ✅ |
| Branch / review | Sunucu-taraflı branch modeli, `/review` UI, risk sınıflandırması, Run Tests (gerçek ephemeral container), Affected Code | ✅ |
| Ephemeral DB provisioning | Run Tests'in ham `Docker.DotNet` mekanizması | ✅ **preview DB'nin temeli bu** |

**Engel:** `Github:AppId`, `Github:PrivateKey`, `Github:WebhookSecret` tanımlı
değil. Bot kimlik bilgisi yoksa yazmayı **denemiyor** — sahte başarı
raporlamıyor. (Bkz. 34 §8.)

---

## 2. Eksik olan tek kavram

**Depo, birinci sınıf bir nesne değil.** Bot bir PR olayını işleyebiliyor ama
"bu proje şu depoya bağlı" diye bir kayıt yok; dolayısıyla depo taranamıyor,
branch'e DB eşlenemiyor, geri yazılamıyor.

Planın omurgası bu bağ:

```
ProjectRepository
  ProjectId            hangi Namines projesi
  Owner / Name         acme/shop
  InstallationId       GitHub App kurulumu (public repo'da null)
  DefaultBranch        main
  SchemaPath           .namines/schema.nsl (veya tespit edilen ayrıştırıcı kaynağı)
  WriteMode            none | pull-request | push      ← bkz. 04
  WriteApprovedAt/By   izin butonunun kanıtı
```

Her katman bu kaydın bir alanını kullanıyor. Kayıt yoksa hiçbiri çalışmıyor.

---

## 3. Katmanlar ve sıra

| # | Katman | Ne | App gerekir mi | Doküman |
|---|---|---|---|---|
| A | **Okuma** | GitHub linki → depo taraması → şema / drift | ❌ public için hayır | [01](01-DEPO-TARAMA.md) |
| B | **Bot canlı** | PR'da risk yorumu + check + **preview DB** | ✅ | [02](02-BOT-VE-PREVIEW-DB.md) |
| C | **Çoklu geliştirici** | Branch↔DB, 3-yollu semantik merge, merge kuyruğu | ✅ | [03](03-COKLU-GELISTIRICI-MERGE.md) |
| D | **Yazma** | Canvas → PR; izin onayından sonra doğrudan push | ✅ | [04](04-YAZMA-YOLU-VE-IZIN.md) |
| E | **CI/CD ve gerisi** | Action, CLI, GitHub ile giriş, eject → yeni repo | kısmen | [05](05-CI-CD-VE-DIGER.md) |
| F | **Eklenti mimarisi** | `ISchemaSource` kayıt defteri + `+` kaynak menüsü | ❌ | [06](06-EKLENTI-MIMARISI.md) |

**Fazlandırma: [07-FAZ-PLANI.md](07-FAZ-PLANI.md).**

**Karar: ilk teslim edilecek dilim A.** Sebebi: GitHub App'i (senden bir hesap
işi) beklemez, var olan üç ayrıştırıcıyı kullanır, tek yeni şey depo taraması,
ve tek başına gösterilebilir bir değer üretir — *"linki yapıştır, şemanı gör"*.

---

## 4. Alınmış kararlar

| Karar | Seçim | Not |
|---|---|---|
| Yazma sınırı | PR **ve** açık izin onayından (buton) sonra doğrudan push | Ayrıntı ve koruma bantları: [04](04-YAZMA-YOLU-VE-IZIN.md) |
| Preview DB nerede | Var olan ephemeral container (Run Tests mekanizması) | Ground gelince arkası değişir, sözleşme değişmez |
| İlk dilim | Public repo linki → şema | App beklemez |

---

## 5. Değişmeyen kurallar

- **Kullanıcının canlı/prod veritabanına Namines yazmaz.** Üretilen migration
  ya kullanıcının CI'ı ya da açık onaylı Change Review üzerinden uygulanır.
  Bu, 12-ENTEGRASYONLAR'daki güven modelinin temeli — depoya yazma izni bunu
  değiştirmiyor: **depo ≠ veritabanı.**
- **AI'ya düşülmez.** Tanınmayan format dürüstçe reddedilir, uydurulmaz.
- **Kısmi sonuç dürüstçe raporlanır** — `parsedCount` / `skippedCount` /
  atlanma nedeni. Depo taramasında bu kural daha da önemli: 5000 dosyalık bir
  monorepo'da "şemanı çıkardım" demek, 4800 dosyayı görmediğini söylemeden
  yalan olur.
- **Kimlik bilgisi yoksa yazma denenmez.** Çalıştığı sanılan ama hiçbir şey
  yapmayan özellik, hiç olmayandan kötüdür.

---

## 6. Riskler

| Risk | Karşılık |
|---|---|
| **İzin tırmanması** — `contents:read` → `contents:write` → `pull_requests:write`. Her biri ayrı bir güven olayı. | Kapsam **özellik başına** istenir, hepsi baştan değil. Kullanıcı hangi izni neden verdiğini ekranda görür. |
| **Preview DB maliyeti** | TTL, PR kapanınca silme, proje başına eşzamanlı sayı kotası (36-KOTA-VE-AJAN). |
| **Preview DB'ye prod verisi sızması** | Varsayılan: **boş şema + mock veri.** Gerçek veri kopyalama ayrı, açıkça onaylanan bir özellik. |
| **Rate limit / dev monorepo** | 200 dosya / 2 MB bütçesi korunur; tree API tek çağrıda alınır, dosya içerikleri sadece adaylar için çekilir. |
| **Docker Desktop bu makinede kırılgan** | Preview DB'ler sıralı provision edilir; paralel ağır container WSL2'yi çökertiyor (AGENTS.md). |
| **Doğrudan push'un gözden kaçan değişiklik üretmesi** | Yıkıcı (destructive) değişiklik **izin ne olursa olsun** PR'a düşer. Bkz. [04 §3](04-YAZMA-YOLU-VE-IZIN.md). |
