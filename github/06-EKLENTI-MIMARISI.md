# 06 — Eklenti Mimarisi ve "+" Kaynak Menüsü

> **Amaç:** GitHub'ı tek başına bir özellik olarak değil, **ilk tam eklenti**
> olarak yazmak; ardından bugün dağınık duran kaynakları (OpenAPI, resim, ses,
> kod dosyası, DB bağlantısı) aynı sözleşmeye taşımak.

## 1. Önce dürüst bir tespit

**Dokümanlarda "extension" geçiyor ama bu bizim kastettiğimiz eklenti değil.**
`second-phase/06-VERI-KAYNAKLARI.md`'deki "extension", JSON trafiğini gözlemleyen
bir **tarayıcı eklentisi** — bir taşıyıcı. Kod tabanında da bir eklenti/kayıt
defteri kavramı yok (`plugin`, `connector`, `registry` diye bir soyutlama
aranmadı değil, **yok**).

Yani: **eklenti mimarisi yeni bir tasarım.** Var olanı kullanıyormuş gibi
yazmak, sonra implementasyonda "aslında yokmuş" diye bulmak bu projede bir kez
yaşandı (gözlem kaydı #2). O yüzden açıkça: bu bölüm sıfırdan.

## 2. Bugün dağınık duran kaynaklar

Hepsi **çalışıyor**, hiçbiri ortak bir sözleşmeye bağlı değil:

| Kaynak | Nerede | Ne üretir |
|---|---|---|
| Düz metin prompt | `/new` | AI ile şema |
| Resim (vision) | `VisionUploadModal` | AI ile şema |
| Ses (Whisper) | `/new` | prompt metni |
| **OpenAPI / GraphQL** | `ApiSpecExtractor`, `/new` içindeki link butonu | Şema (tahmin etiketli) |
| Gözlenen JSON şekli | `JsonShapeInferencer`, `POST /api/codeschema/infer-shapes` | Şema (tahmin, `isGuess`) |
| Kod dosyaları | `CodeImportPanel`, `CodeSchemaExtractor` | Şema (deterministik) |
| Canlı DB bağlantısı | `DbConnectionPanel`, `POST /api/dbintrospect` | Şema (kesin) |
| Başlangıç şemaları | Canvas | Şema (sabit) |
| **GitHub** | — | [01](01-DEPO-TARAMA.md) ile gelecek |

Sorun: her biri **ayrı bir düğme, ayrı bir modal, ayrı bir sözleşme.** Kullanıcı
"şemamı nereden getirebilirim" sorusunun cevabını tek bir yerde göremiyor,
ve yeni kaynak eklemek her seferinde UI'da yeni bir yer bulmak demek.

## 3. Sözleşme: `ISchemaSource`

```
ISchemaSource
  Id                "github" | "openapi" | "dbconnect" | "code" | "vision" | ...
  DisplayName       "GitHub"
  AuthKind          none | url | credential | oauth-app-install
  Capabilities      Import | Compare | Watch | WriteBack     (bayrak)
  ConfigSchema      hangi alanları soracağı (UI bunu okuyup formu üretir)

  ImportAsync(config)      → SourceResult
  CompareAsync(config, schema) → ImpactReport        (Compare varsa)
  WatchAsync(config)       → drift olayı             (Watch varsa)
  WriteBackAsync(config, schema) → yazma sonucu      (WriteBack varsa)
```

`SourceResult` **her kaynakta aynı dürüstlük alanlarını taşımak zorunda:**

```
SourceResult
  Schema          DatabaseSchema
  IsGuess         çıkarım mı, kesin mi
  ParsedCount / SkippedCount
  Skipped[]       { item, reason }        ← neden atlandığı ZORUNLU
  Warnings[]
```

**Sözleşmenin asıl değeri burada.** Bugün "atlananı dürüstçe bildir" kuralı her
ayrıştırıcıda ayrı ayrı hatırlanması gereken bir disiplin; sözleşmeye konunca
**unutulamaz hâle geliyor** — alanı doldurmayan kaynak derlenmiyor.

### Yetenek matrisi

| Kaynak | Import | Compare | Watch | WriteBack |
|---|:-:|:-:|:-:|:-:|
| GitHub | ✔ | ✔ | ✔ | ✔ (izinle, [04](04-YAZMA-YOLU-VE-IZIN.md)) |
| DB bağlantısı | ✔ | ✔ | ✔ | ✖ (güven sınırı) |
| Kod dosyaları | ✔ | ✔ | ✖ | ✖ |
| OpenAPI / GraphQL | ✔ | ✔ | ✖ | ✖ |
| JSON şekli | ✔ | ✖ | ✖ | ✖ |
| Resim / ses / metin | ✔ | ✖ | ✖ | ✖ |

UI hiçbir yerde "bu kaynak bunu yapabilir mi" diye elle bilmiyor — bayraklara
bakıyor. Yeni eklenti eklendiğinde menü kendiliğinden güncelleniyor.

## 4. "+" menüsü

Prompt kutusunun yanındaki dağınık ikonlar **tek bir belirgin `+`** altında
toplanıyor:

```
[ Şemanı anlat…                                      ]
[ + Kaynak ekle ]  [ 🎤 ]  [ Üret → ]

  + Kaynak ekle
  ─────────────────────────────
  BAĞLA            (sürekli ilişki)
    GitHub deposu          ← public link veya App kurulumu
    Veritabanı bağlantısı
  ─────────────────────────────
  İÇE AKTAR        (tek seferlik)
    OpenAPI / GraphQL URL
    Kod dosyaları (Prisma · EF Core · SQL)
    Resim
    Örnek JSON yanıtı
  ─────────────────────────────
  BAŞLANGIÇ
    Hazır şemalar (5)
```

- **"Bağla" ile "İçe aktar" ayrımı bilinçli.** Bağlananlar `Watch` yeteneğine
  sahip — arkadan drift takip edilir. İçe aktarılanlar tek seferliktir. Kullanıcı
  bu farkı menüde görmeli, sonradan "neden drift bildirimi almıyorum" diye
  sormamalı.
- Eklenen her kaynak prompt kutusunun altında **rozet** olarak durur
  (`GitHub: acme/shop@main ✕`), birden çok kaynak birleştirilebilir.
- Tahmin üreten kaynakların rozeti farklı renk + "tahmin" etiketi taşır —
  06-VERI-KAYNAKLARI'nın "çıkarım olduğu her ekranda söylenmeli" kuralı.

Ayarlarda ayrıca bir **"Eklentiler"** sayfası: kurulu kaynaklar, verilen izinler,
izin geri alma, son senkron zamanı.

## 5. Bir repodaki "veritabanlarını görmek" — ne mümkün, ne değil

Kullanıcının isteği: *"bir repodaki public veya private DB'leri görelim, her
branch'inde DB'sini görelim."* Bunun üç ayrı okuması var ve ikisi mümkün:

| Okuma | Mümkün mü | Nasıl |
|---|---|---|
| Branch'teki **koddan çıkan şema** | ✅ | [01](01-DEPO-TARAMA.md) — her branch ayrı taranır, yan yana gösterilir |
| Branch için **Namines'in açtığı canlı DB** | ✅ | `IBranchDatabaseProvisioner` zaten var ([02 §4](02-BOT-VE-PREVIEW-DB.md)) |
| Kullanıcının **kaydettiği** bağlantının branch'e eşlenmesi | ✅ | Bağlantı sırrı `AesGcmConnectionSecretProtector` ile şifreli saklanıyor; branch→bağlantı eşlemesi eklenir |
| Repo'daki **gerçek üretim DB'sine** repo üzerinden ulaşmak | 🔴 **Hayır** | Aşağı bak |

**Neden son madde hayır:**

- GitHub Actions secret'ları **API ile okunamaz** (yazma yönlüdür). Repo'daki
  `DATABASE_URL`'i alıp bağlanmak teknik olarak mümkün değil.
- `.env` dosyası repoya commit'lenmişse teknik olarak okunabilir — **ama
  okumayız.** Kullanıcının sızmış kimlik bilgisini kullanmak, onu bulduğumuzu
  söylemekten bile önce gelen bir güven ihlali olur. Doğru davranış:
  *"deponda commit'lenmiş bir `.env` gördük, içeriğini okumadık, silmelisin."*
- Bağlantı her zaman **kullanıcının açıkça verdiği** bir şeydir. Bu, ürünün
  SSRF/no-persistence güvenlik modelinin (AGENTS.md) devamı.

## 🔴 Yapılmayacak

- Üçüncü tarafların yazdığı eklentileri çalıştırmak (pazar yeri, keyfi kod).
  Buradaki "eklenti", **bizim yazdığımız kaynakların ortak sözleşmesi** —
  çalışma zamanı sandbox'ı değil. O ayrı ve çok daha büyük bir iş.
- Repodaki sırları okumak.
- Bir kaynağı, `Skipped`/`IsGuess` alanlarını doldurmadan kayıt defterine almak.
