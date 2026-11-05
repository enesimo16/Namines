# Faz 14.2C Bağımsız Kabul Denetimi

> Denetlenen: `arch/phase-14-2`, ikinci tur `1a9c6a0..cb4d3a4`, birinci tur `322d416..f4f9009`. Tarih: 2026-11-05.
> Ortam: Windows 11, .NET 8.0.427, Docker; kendi `postgres:16` (Debian, ICU'lu, 16.15) container'ım, testlerde Testcontainers (`postgres:17-alpine`). Uygulayıcının rapor ve test çıktılarına güvenilmedi; aşağıdaki her sayı bu denetimde üretildi. Kod düzeltilmedi.

## Nihai karar (ikinci tur, `cb4d3a4`): ACCEPTED

B-1, M-1, M-2 ve M-3 kapandı; L-1, L-3, L-4, L-5, L-7 kapandı ya da gerekçeli karara bağlandı. Kabul kriterlerinin hepsi sağlanıyor, 14.2D-1 başlayabilir. Kalanlar bloklamıyor ve aşağıda faza atandı.

**Sınırlar:** frontend bu turda koşulmadı (`322d416..HEAD` aralığında `frontend/` ve `services/` diff'i boş, ilk turun sonucu geçerli). Karşılaştırma yoklama temelli: 2,4 milyon karar her olası istemciyi kapsamaz.

### Kabul tablosu (ikinci tur)

| Alan | Sonuç | Kanıt |
|---|---|---|
| Build | PASS | `dotnet build backend/Namines.sln -c Release --no-incremental`: 0 uyarı, 0 hata |
| Unit + Integration | PASS | `Namines.Tests`, sıralı: 2898 toplam, 2898 geçti, 0 kaldı, 0 atlandı, 15 dk 56 sn. Gerçek PG ve Docker ayakta |
| RunTests | PASS | `Namines.Tests.RunTests`, sıralı: 19/19, 1 dk 1 sn |
| Architecture | PASS | Aynı koşuda yeşil |
| Migration | PASS | 90.000 fuzz anahtarıyla `--migrate` → runbook Down → `--migrate`: rc=0, Up1 = Up2 **0 fark**. `has-pending-model-changes` Auth, Studio, Hub, Gateway temiz |
| Data integrity | PASS | Bayrak uyuşmazlığı 0; C#'ta olmayan satır 0; satıra dönmeyen C# girdisi 0. Hacimde Down sonrası 10.000 + 10.000 ham liste, 200.000 etiket, 50.000 maske geri geldi |
| Security | PASS | IP ve origin genişlemesi 0 (aşağıda). B-1 HTTP: 403. Tek bilinçli fark L-1 |
| API compatibility | PASS | Rota ve alan değişmedi. Duman 75/75. Yeni fark 409 raporda (L-3) |
| Performans | PASS | Sorgu kodu bu turda değişmedi, ilk tur ölçümü geçerli. `--migrate` 200 bin issue: 39,5 sn |
| Documentation | PASS, bir not | Rapor düzeltmeleri doğru, 2898/19 benimkiyle aynı. `IMPLEMENTATION_STATUS.md:3,18` hâlâ "denetim bekliyor": bu commit'in kapsamı dışında, sıradaki doküman işinde güncellenmeli |
| Independent audit | **ACCEPTED** | Bu belge |

### Bulguların yeniden sınanması

| ID | Durum | Kanıt |
|---|---|---|
| B-1 | **Kapandı** | Aşağıda. Uygulayıcının fuzz'ı kullanılmadı |
| M-1 | **Kapandı** | Gerçek migration'ın kurduğu tetikleyici (`FOR UPDATE` 1 eşleşme). Yarış, aynı anda 4 issue silinerek: katalog 1, işlenmiş bağ 1. Son bağ silinince katalog 0. Test `Etiket_temizligi_...` T2'nin beklediğini de doğruluyor |
| M-2 | **Kapandı** | Runbook komutu yazıldığı gibi (`Command Timeout=1800`, `--no-build` yok): 200 bin issue, 20.000 karantina satırı: rc=0, derleme dahil 37 sn. Sonuç doğru, tablolar gitti, karantina 0, geçmiş `20261105105313`. Kök neden olarak karesel aramanın geçici tabloya alınması kodda doğru |
| M-3 | **Kapandı** | xUnit tanılaması: `Starting: Namines.Tests (parallel test collections = off, ...)`. TRX'te sınıf geçişi 839'dan 300'e (229 sınıf; kalan, ms yuvarlaması ve koleksiyon içi sıra). `PrismaAvailable` artık `WorkingDirectory` veriyor, `CoderAiPackageSecretsTests` paralel olmayan koleksiyonda. Tam koşu temiz |
| L-1 | Karar, kabul | Kod değişmedi, rapor §5'te gerekçeli, test belgeliyor. Bende 5366 yoklamanın hepsi aynı adres + kapsam kimliği |
| L-3 | Karar, kabul | 409 §4'te. Duman: 8 eşzamanlı `PUT`'ta 3 tane, maske sonrası duruyor |
| L-4 | **Kapandı** | Gerçek sınama: açık işlem varken `--migrate` 31 sn'de `55P03` ile düştü, sıfır olmayan çıkış, şema değişmedi; işlem kapanınca yeniden deneme rc=0. Testin kendisi elle yazılmış `LOCK` kullanıyor, gerçek davranışı bu ölçüm gösteriyor |
| L-5 | **Kapandı** | Yalnız boşluk için iz yok, koşul `namines_2c_trim(...) <> ''` |
| L-7 | **Kapandı** | `ByodbHardeningTests`, `DdlExecutionTests`, `GatewayWriteExecutionTests`, `Phase14_2BMigrationTests` eklendi |

### Bağımsız fark testi

Kendi dilbilgisi üretecim (mutasyon değil, parçadan kurma), sabit tohum. Kâhin olarak `git show 322d416:.../GatewayKeyRestrictions.cs` **derlendi**, elle kopya değil. Yeni taraf: gerçek `--migrate` ile dolan satırlar, gerçek `GatewayKeyRestrictions`.

| Ölçü | Sonuç |
|---|---|
| Girdi | 60.000 IP listesi + 30.000 origin listesi; satır 27.372 IP, 7.950 origin; tümü geçersiz liste 48.651 |
| Up doğrulaması | Yeni el yazısı ayrıştırıcı doldurmayla çelişmedi, migration düşmedi (yanlış alarm 0) |
| IP karşılaştırma | 1.968.028 karar (güvenilir ve güvenilmez), **genişleme 0**. Kapsam kimlikli 5366 (L-1). Daralma 78.649, bilinçli |
| Origin karşılaştırma | 434.809 karar, **genişleme 0**. Daralma 76.169, bilinçli |
| Bayrak | Uyuşmazlık 0. İstemcisiz (null) durumda fark 0 |
| Down sonrası eski kod | Geri dönen metin ile özgün metin eski kodda: 1.461.290 karar, **genişleme 0**, daralma 6 |
| Üretecin gücü | Karantinadaki 5388 girdiyi çıplak `inet` kabul ediyor (`::FFFF:188.40`, `::FFFF:74.132.212` gibi): B-1 sınıfına gerçekten ulaşıldı ve hepsi kural olmadı |

### B-1 gerçek HTTP (duman 75/75, API logunda `ada@secret.example` ve parola 0)

`--migrate` sonrası, `X-Forwarded-For` güvenilir ağdan: `::ffff:1.2.3` için `1.2.3.0` → **403** (önceki tur 200). `::.2.3.4/0` için `2001:db8::1` ve `2600:1f18::77` → **403** (önceki tur 200). API `::ffff:1.2.3` → 400. Çocuk tablolar okunamazken yine 500, veri yok.

### Test değişiklikleri zayıflatma mı?

- **Kartezyen teori → iki Fact: zayıflatma yok.** Listeler ve yoklamalar aynı, yalnız 5 değer eklendi; `trustworthy` iki yönü duruyor; `Assert.Equal` → `Assert.True(legacy == current, mesaj)` aynı karar. Fark: ilk hatada durur. Silinen başka doğrulama yok (`git diff`: yalnız bu 4 `Assert` ve 2 `[Theory]`).
- **Toplam 4675 → 2898** bu yüzden; ilk turda istediğim dürüst sayı.
- **Doğrulama bağımsızlığı:** `Dogrulama_doldurma_...` korumayı söküp UpVerify'ın `verify ips` ile patladığını gösteriyor. Kırpma ve etiket anahtarı hâlâ ortak, rapor bunu açıkça yazıyor.
- **`GatewayMaskingEndpointTests` gerçek:** gerçek PG, gerçek `GatewayController` ve `GatewayService`, maskesiz kontrol tablosu. İlk turda sınayamadığım `export`/`detail` sınırını kapatıyor. Geçici worktree'de mutasyon (silindi):

| Mutasyon | Sonuç |
|---|---|
| Değişiklik yok | 6/6 |
| `MaskedColumnsAsync` boş döner | 5 kaldı (list, detail, export csv/json, expand); kontrol testi geçti |
| Yalnız expand yolu maskelemez | 1 kaldı (expand) |
| Yalnız ana yol maskelemez | 4 kaldı (list, detail, export csv/json) |

### Backlog (bloklamayan, faza atanmış)

| ID | Sev. | Madde | Faz |
|---|---|---|---|
| BL-C1 | MEDIUM | `repo_issue_labels`'a bileşik FK ile proje bütünlüğü (bugün yalnız tek yazma yolu ve doğrulama koruyor) | 14.2D-1 (Hub) |
| BL-C2 | LOW | L-2: karantina ham metni anahtar, proje ve hesap silinince duruyor; saklama süresi ya da silme akışı | 14.2D-2 (Identity) |
| BL-C3 | LOW | L-6: anahtar sorgusu çarpımı eski büyük listelerde tavansız; gerekirse `AsSplitQuery` | 14.2D-1 (Gateway) |
| BL-C4 | LOW | `RepoIssueLabelStore` Hub'a taşınması; `GatewayDbContext`'in gerçekten kullanılması | 14.2D-1 |
| BL-C5 | LOW | L-3: maske `PUT` çakışmasında tek yeniden deneme (isteğe bağlı) | 14.2D-1 (Gateway) |
| BL-C6 | LOW | Migration zinciri olmayan 8 bağlam `has-pending-model-changes`'te "changes" diyor; sahiplik taşınırken anlamlı hâle getirilmeli | 14.2D-1 / 14.2D-2 |
| BL-C7 | BİLGİ | I-1: Unicode 16 harflerinde ICU/.NET etiket anahtarı farkı (27 kod noktası) | Sonraki .NET yükseltmesi |
| BL-C8 | LOW | `IMPLEMENTATION_STATUS.md` 14.2C durumu güncellenmeli | 14.2D-1 başlangıcı |

---

# Birinci tur (`322d416..f4f9009`): kayıt

## Birinci tur kararı: CONDITIONALLY ACCEPTED

Normalizasyon tasarımı sağlam ve veri kaybı bulunmadı, ama raporun güvenlik iddiası ("geçersiz girdi asla genişletmez") **yanlış**: eski kodun hiçbir istemciyle eşleştirmediği bazı IP kuralları migration'dan sonra erişim veriyor (B-1, gerçek HTTP ile kanıtlı). Migration henüz hiçbir yere gitmedi, bu yüzden düzeltme aynı dosyada yapılmalı. B-1, M-1, M-2, M-3 kapanmadan 14.2D-1 başlamaz; dar kapsamlı yeniden denetim yeterli.

## Kabul tablosu

| Alan | Sonuç | Kanıt |
|---|---|---|
| Build | PASS | `dotnet build backend/Namines.sln -c Release --no-incremental`: 0 uyarı, 0 hata, 30 sn |
| Unit + Integration | PASS, bir not | `Namines.Tests`: 4675 toplam, 4674 geçti, 1 kaldı, 0 atlandı, 19 dk 34 sn. Kalan `PrismaGeneratorTests...(engine: SQLite)`: kök neden bulundu (M-3), fazla ilgisi yok. Sınıf yeniden koşuldu: 21/21. `Namines.Tests.RunTests`: 19/19, 1 dk 16 sn |
| Architecture | PASS | Aynı koşuda; `Phase14_2CNormalizationArchitectureTests` yeşil. `GatewayDbContext` üretimde kullanılmıyor (D-1) |
| Migration | PASS, B-1 hariç | Kötü niyetli seed, `--migrate` → `dotnet ef` Down → `--migrate`: rc=0, Up1 = Up2 **0 fark**. Katalog: `old_columns=0`. NOT VALID kısıtlar aynen geri geldi |
| Data integrity | PASS, B-1 hariç | Etiket, maske, sıra ve bayrak alan alan doğru. CSV ayrıştırma paritesi: 20.000 liste 0 fark. Kırpma kümesi: 63.485 BMP karakter 0 fark |
| Security | **FAIL** | B-1. Bunun dışında: maske, origin, tablo okunamazken 500 (fail-closed), mass assignment, log'da sır 0 |
| API compatibility | PASS | Alan ve tipler aynı. `next build`, `tsc`, vitest geçti. Yeni fark: eşzamanlı maske `PUT`'unda 409 (L-3) |
| Performans | PASS | EF'in gerçek SQL'i üzerinde ölçüm, aşağıda |
| Frontend | PASS | `tsc` 0 hata; `eslint` 0 hata 188 uyarı (frontend bu aralıkta değişmedi); vitest 39 dosya 280/280; `next build --webpack` rc=0. `services/desk` değişmedi, koşulmadı |
| has-pending-model-changes | PASS (4 bağlam) | Auth, Studio, Hub, Gateway temiz. Identity, Billing, Governance, Ops, Vault, Flow, Desk, Ground "changes" diyor: bu bağlamların migration zinciri ve snapshot'ı yok, DDL sahibi Auth; bu fazın kusuru değil, D aşamalarında anlam kazanır |
| Documentation | FAIL (kısmi) | §1 Security satırı ve §3 "bağımsız ayrıştırma" fazla iddialı; rollback komutu hacimde çalışmıyor (M-2). Geri kalanı dürüst |
| Independent audit | **CONDITIONALLY ACCEPTED** | Bu belge |

## Bulgular

### B-1 (BLOCKER, güvenlik): migration bazı "hiçbir zaman eşleşmeyen" IP kurallarını çalışan kurala çeviriyor

**Yer:** `backend/Namines.Infrastructure/Migrations/Phase14_2C_NormalizationScripts.cs:92` (`addr := addr_text::inet`).

**Neden:** PostgreSQL `inet`, IPv6 içindeki gömülü IPv4'te eksik oktet kabul ediyor; .NET `IPAddress.TryParse` reddediyor. Eski kodda ayrıştırılamayan kural eşleşmez. `Ipv6CharsPattern` nokta içeren kuyruğu denetlemiyor. Up doğrulaması aynı fonksiyonu kullandığı için göremiyor. Parite tablosunda da bu biçim yok.

| Eski metin | Eski kod | Migration sonrası |
|---|---|---|
| `::ffff:1.2.3` | kimse | `1.2.3.0/32` |
| `::ffff:1.23` | kimse | `1.23.0.0/32` |
| `::.2.3.4/0` | kimse | **`::/0`, tüm IPv6** |
| `::ffff:62:.253.88.177/46` | kimse | `::/46` |

Fuzz: 51.569 farklı girdi, SQL'in kabul edip C#'ın reddettiği **105 girdinin 105'i** gerçek genişleme, hepsi noktalı. Noktasız 0.

**Repro (gerçek API, `--migrate` sonrası, `X-Forwarded-For` güvenilir ağdan):**
- `AllowedIps='::ffff:1.2.3'`: `1.2.3.0` → **200** (`1.2.3.1` → 403). Eski kod: 403.
- `AllowedIps='::.2.3.4/0'`: `2001:db8::1` ve `2600:1f18::77` → **200**. Eski kod: 403.
- Down da onarmıyor: bu kurallar karantinada olmadığı için metin `::/0` olarak geri dönüyor, eski kod da artık tüm IPv6'ya izin veriyor (`ki49`).

API ucu `NormalizeIp` ile doğru reddediyor (`::ffff:1.2.3` → 400); açık yalnız migration'da. Eski API her metni kabul ettiği için böyle bir satır üretimde olabilir. Olasılık düşük, ama fazın tek önceliği buydu ve bedeli bir satır.

**Gerekli düzeltme:** `namines_2c_ip`'te `::inet`'ten önce, nokta varsa kuyruğu tam dört oktet iste. Aşağıdakini kendi fuzz'ımda denedim: SQL'e özgü kabul **105 → 0**.
```sql
IF position('.' in addr_text) > 0 AND addr_text !~ '{{Ipv4Pattern}}'
   AND addr_text !~ '^[0-9a-fA-F:]*:(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}$'
THEN RETURN NULL; END IF;
```
`namines_2c_origin` köşeli parantezi için de aynısı (orada genişleme yok, tam dizge eşleşmesi; 27 fark yalnız C#/SQL kabul farkı). `GatewayAllowlistCases.IpLists`'e yukarıdaki dört değer eklensin. Gerçek PG testinde bu listeler `Restrict*=true`, satır yok çıkmalı.

### M-1 (MEDIUM): GC tetikleyicisi yarışta işlenmiş bağı siliyor

**Yer:** `Phase14_2C_NormalizationScripts.cs:343-349`. **Repro (nasty DB):** T1 başka bir issue'ya var olan etiketi bağlar, commit etmez. T2 etiketin son issue'sunu siler: tetikleyici T1'in satırını görmez, katalog `DELETE`'i T1'in `KEY SHARE` kilidinde bekler. T1 commit eder, `DELETE` devam eder, CASCADE T1'in **işlenmiş** bağını siler. Sonuç: katalog 0, `gcY` bağı 0, hata yok.

**Bugün tetiklenemiyor:** tek tek issue silen üretim kodu yok (grep: `RepoIssues.Remove/ExecuteDelete` 0); proje silmede issue da gidiyor. Ters sırada T1 FK hatası alır ve `CreateIssue` yeniden dener, doğru. Ama silme ucu eklendiği gün sessiz veri kaybına döner ve migration hâlâ açıkken düzeltmesi bedava.

**Gerekli düzeltme:** `DELETE`'ten önce `PERFORM 1 FROM hub.issue_labels WHERE "Id" = OLD."IssueLabelId" FOR UPDATE;`. Aynı senaryoda denendi: katalog 1, bağ 1; son bağ silinince katalog 0. Eşzamanlılık testi eklensin.

### M-2 (MEDIUM): belgelenen geri alma komutu hacimde çalışmıyor

**Yer:** `docs/deploy/MIGRATION-VE-GERI-ALMA.md` §5.2. `Database:MigrationCommandTimeoutSeconds` yalnız uygulama içi migration yolunda (`Program.cs`, `--migrate`). **Repro:** 200 bin issue + 20 bin karantina satırında `dotnet ef database update 20261105105313_...`: `Failed executing DbCommand (30,023ms) ... CommandTimeout='30'`, rc=1. İşlem geri alındı, veri sağlam, ama rollback yapılamıyor. `Command Timeout=570` eklenen bağlantı dizesiyle: rc=0, 100 sn.

**Gerekli düzeltme:** runbook'a bağlantı dizesinde `Command Timeout=1800` ekle (en azından) ve testi geçen komutu yaz. Rapor §3'e Down süresi eklensin.

### M-3 (MEDIUM, test altyapısı): "sıralı" koşu sıralı değil; Prisma düşüşünün kök nedeni bu

- `-- xunit.parallelizeAssembly=false xunit.parallelizeTestCollections=false` **etkisiz**. TRX'te `PrismaGeneratorTests` satırı `McpHardeningTests`'in teori satırlarının arasında (±4 ms); `xunit.runner.json` ve `CollectionBehavior` yok. Raporlardaki komut (ve 14.2B denetimindeki "sıralı" ifadem) yanlış anlam taşıyor.
- `CoderAiPackageSecretsTests.cs:34` süreç geneli `Directory.SetCurrentDirectory` yapıyor, sonra dizini siliyor; `PrismaAvailable.cs:104` `WorkingDirectory` vermeden `cmd.exe` açıyor: "The current directory is invalid". Üretim kodu değil, 14.2C'den önce de var.
- **Gerekli düzeltme:** `PrismaAvailable.Run`'a `WorkingDirectory` (geçici dizin) ver, `CoderAiPackageSecretsTests`'i paralel olmayan koleksiyona al ya da paketleyiciye çıktı yolu geçir. Sıralılık isteniyorsa `xunit.runner.json` kullan; raporlardaki komutu düzelt.

### Düşük ve bilgi

| ID | Sev. | Bulgu | Gereken |
|---|---|---|---|
| L-1 | LOW | Teknik genişleme: düz kural `fe80::1`, kapsam kimlikli istemci `fe80::1%3`. Eski `IPAddress.Equals` kapsamı karşılaştırıyordu (ret), yeni `/128` bayt karşılaştırıyor (izin). Fuzz'da 1767 yoklamanın hepsi bu desen, kapsamsız genişleme 0. Aynı adres, eski kodda `fe80::1/128` zaten izin veriyordu | Bilinçli karar olarak rapora yaz; kod şart değil |
| L-2 | LOW | Karantinada kalan ham metin (`raw`, `entry`, `project_id`) anahtar, proje ve hesap silindikten sonra da duruyor: duman sonrası anahtar 0, satır 5. Sır değil ama IP adresi ve `user:pw@` gibi kullanıcı metni olabilir. `namines_app` yetkisiz | `DataRetentionService` ya da silme akışına temizlik; D-2 |
| L-3 | LOW | Eşzamanlı maske `PUT`'u UNIQUE'e takılıp 409 dönebiliyor (8 istekte 2-3 kez), rapor §4'te yok. Eski kod çakışmıyordu. Durum güvenli: işlem geri alınıyor, maske duruyor | §4'e yaz; istenirse bir kez yeniden dene |
| L-4 | LOW | `LOCK TABLE` için `lock_timeout` yok: açık kalmış bir işlem varsa migration süresiz kuyrukta bekler | `SET LOCAL lock_timeout='30s'` |
| L-5 | LOW | `blank_list_means_no_restriction` yalnız boşluktan oluşan metin için de yazılıyor (`:281,288`, koşul `<> ''`); §3 tablosu ilk satırda "kısıt yok" diyor, iz demiyor. Operatöre yanlış alarm | Koşula `btrim`/items ayrımı ya da belgeye not |
| L-6 | LOW | Tek anahtar için tek sorgu `AsSingleQuery` (`GatewayAccess.cs:81`): 64x64 listede 4096 satır, 3,5 ms ve bellek. Eski listelerde 64 tavanı yok (80x80 seed geçti); çarpım tavansız | Büyük liste görülürse `AsSplitQuery`; D-1 |
| L-7 | LOW | `PostgresTestPools.Reset` yalnız 13 sınıfta; `ByodbHardeningTests`, `DdlExecutionTests`, `GatewayWriteExecutionTests`, `Phase14_2BMigrationTests` dışarıda | Container başlatan ortak yardımcıya taşı |
| I-1 | BİLGİ | Etiket anahtarı: 128.996 kod noktasında 27 fark (Unicode 16 harfleri: `U+1C89`, `U+A7CB..`, `U+10D50..`). ICU küçültüyor, .NET 8 küçültmüyor. Etkisi: bu harfli eski etikette filtre kaçırır, ikinci katalog satırı açılır. Pratikte yok sayılabilir | Gerekirse not |
| I-2 | BİLGİ | Gömülü v4'te baştaki sıfır (`::ffff:93.134.85.07`): .NET kabul, SQL ret (19 girdi). Yönü daraltma, fail-closed. Kanonik metin 11 girdide farklı (`::ffff:0:1.2.3.4` / `::ffff:0:102:304`), anlam aynı, yalnız UNIQUE yinelemeyi yakalamaz | Yok |

## Sorulara yanıtlar

### 1. Gateway semantiği

**Maske:** okunmamış satır yolu bulunmadı.
- Karar kodu (`MaskValues`, `OrdinalIgnoreCase`) ve `GatewayController.cs` bu aralıkta değişmedi. Tek okuyucu `MaskedColumnsAsync`, iki çağrı (`:288`, `:356`), önbellek yok.
- Duman: maskeli okuma, `EPOSTA` ile büyük harf, güncelleme, sıra değişimi, temizleme, >512 → 400 ve eski maske duruyor, izin silince çocuk CASCADE, 8 eşzamanlı `PUT` sonrası maskeli.
- Çocuk tablo adı değiştirilince: **500**, veri yok, bağlantı metni yok. Origin ve IP tabloları için de 500, 200 değil. Fail-closed doğrulandı.
- `export` ve `detail`'i gerçek veriyle sınayamadım (403/400, anahtar yetkisi ve benim isteğim); kod yolu değişmediği için güveniyorum, yine de sınırdır.

**Origin:** genişleme **0** (18.615 girdi, 10'ar yoklama). Karşılaştırma aynı, kanonik metin ASCII küçük harf. `*`, `null`, sondaki `/`, port, Kelvin K, kullanıcı bilgisi, >512: karantina, liste boşsa ret.

**IP:** B-1 ve L-1 dışında genişleme 0, kapsamsız yoklamada 0. `0x7f`, `010.0.0.1`, `127.1`, `/+8`, `/-0`, `/ 8`, `::ffff:x/n`, `%zone`, köşeli, Arapça ve tam genişlik rakam: hepsi red. Host bitli CIDR doğru maskeleniyor.

**Boş → serbest / geçersiz → ret sınırı:** doğru.
- Bayrak `EXISTS(items)`, satır değil. U+00A0 ve U+3000 boşluk (serbest), U+200B ve U+FEFF boşluk değil (ret): C# ile birebir.
- **Bayrak temizleyen yazma yolu yok:** üretimde `ApplyOrigins/ApplyIps` yalnız create'te; `LaunchController` bayraksız, listesiz anahtar üretiyor. `PUT`, `PATCH`, `.../rotate`: 404/405. İptal satırları tutuyor. Request DTO'ya `restrictOrigins` gönderildi: yok sayıldı. Admin ucu yok.
- `!Restrict && Count==0` tek açık kapı: bayrak kapalı ve satır varsa yine uygulanıyor. `Include`'u unutan yeni yol reddeder. Doğru.

**Yalnız virgül serbest kalmalı mı?** Evet. Eski kod öyleydi, kapatmak çalışan bir istemciyi sessizce keserdi. API artık 400 veriyor ve karantina izi var, doğru denge.

### 2. Migration

- **Satır satır:** `DISTINCT ON` ve `min(ord)` sıra koruyor, `ord` boşlar dahil ama `row_number` kesintisiz yapıyor. NULL lateral'de düşüyor. `İ`, `Σ`, `ẞ` beklenen. NOT VALID lift/restore çalışıyor (`RateLimit=0`, 20.001 karakter `Body` seed'de).
- **Doğrulama gücü:** eksik, fazla, yinelenen, yetim, sıra güçlü. Ama "ikinci ve bağımsız ayrıştırma" yalnız ayırma için doğru: normalizasyon **aynı fonksiyon**, o yüzden B-1'i göremedi.
- **Down:** maske ve etiket tam, geçersiz liste ham metinle dönüyor. Kayıplar raporda yazıldığı gibi (kanonik metin, yineleme, boş metin → NULL). Çok uzun maske sona ekleniyor, zararsız.
- **Karantina içeriği:** sır yok (hash, anahtar yok). Bkz. L-2.
- **Kilit:** aynı hacimde benim ölçümüm **36,0 sn** (boşta, `postgres:16` Debian), raporun 110,7 sn'si muhtemelen yük ve alpine etkisi. Dağılım (psql ile ayrı koşu, başka komutlarla aynı anda, toplam 46 sn): en büyüğü etiket bağı 22,8 sn, katalog 5,7 sn, `UpVerify` 9,8 sn. %100 geçersizle 38,6 sn; karantina araması ikinci derece patlamıyor.
- **Gerekli mi?** Bu haliyle evet: runbook eski sürümü önce durduruyor, yani kilit değil kesinti konuşulur. Ucuzlatma mümkün (ayrık etiketlerde anahtarı bir kez hesaplamak, yaklaşık 450 bin → 50 bin çağrı), ama bu hacim gerçek kurulumun çok üstünde, zorunlu değil. İki sürümlü expand/contract çift yazma isterdi, kazancı maliyetine değmez. Tek not: `studio.projects`'te FK için `SHARE ROW EXCLUSIVE` da işlem sonuna kadar sürüyor; §3'e eklenebilir.
- **Katalog:** `old_columns=0`, 5 tablo, indeksler, tetikleyici var. Up1 = Up2 0 fark.

### 3. Etiketler

- **Proje kapsamı:** kabul. Gerekçe doğru, filtre zaten proje içi, sapma raporlanmış.
- **Filtre:** alt dizge → tam eşleşme ve harf duyarsız. `perf` eskiden 1, şimdi 0. Frontend `label` parametresini tipliyor ama `HubIssuesTab` göndermiyor; arama kutusu istemci tarafında `includes` yapıyor ve değişmedi. Depo içi kırılma yok. Dış istemci için davranış değişikliği, §4'te yazılı ve sahibin "etikette LIKE yok" kuralıyla tutarlı: **kabul**, sahibin bilgisine.
- **`labels` çıktısı:** sıra ve yazım issue başına korunuyor. Değişen yalnız boşluk (`"a, b"` → `"a,b"`), harf duyarsız yineleme (`BUG,bug` → `BUG`), boş → null. Frontend `trim` ediyor. Kabul.
- **Proje dışı bağ:** DB düzeyinde **mümkün** (repro: `pA`'daki issue'ya `pB` etiketi, hata yok). Tek yazma yolu projeyle arıyor, API'den ulaşılamıyor, filtre de proje koşullu. Bileşik FK (join'e `ProjectId`, iki tarafta `UNIQUE (Id, ProjectId)`) doğru savunma, **ama bloklamaz:** `RepoIssues` D-1'de taşınırken ekleyin (BL-C1).

### 4. Test sayısı

- **2818 → 4675 (+1857):** **1830'u tek sınıf**, `GatewayAllowlistParityTests` (kartezyen teori); kalan net 27 yeni test. Sayı şişkin ama sahte değil: her satır gerçek karşılaştırma yapıyor. Etkin test yöntemi sayısı birkaç düzine; 4675 başarı ölçüsü sayılmamalı. Koşullu "geçersizse yalnız genişlemedi mi" doğrulaması da liste kadar güçlü: B-1'i kaçırdı.
- **Zayıflatma yok:** silinen 11 satır, `Assert` ya da `Skip` silinmemiş. Değişen doğrulamalar yeni modele çeviri.
- **GatewayAudit:** 8 kez x 7 test = **56/56**. Mekanizmayı ayrı programla kanıtladım: aynı bağlantı dizesi, aynı host portunda container değişimi, havuzlu bağlantı "Exception while reading from stream / bağlantı iptal edildi" verdi; `ClearAllPools` ile geçti. Hata fixture'ın ilk `MigrateAsync`'inde, üretim koduna dokunmuyor. **Maskelenmiş ürün hatası değil.** Port geri dönüşümünü tam koşuda doğrudan yakalayamadım; önceki `full4.log`'da `GatewayApiKeyTests`'te de aynı hata var, tutarlı.

### 5. Repo geneli tarama

Kendi grep'im (`Split(','`, `string.Join`, `LIKE`, `Where+Contains`) ve katalog sorgum (tüm metin/jsonb kolonlar, dizi kolon yok, FK'siz `*Id` metin) §6 ile karşılaştırıldı. **Sorgulanan CSV kalmadı**, gözden kaçan gerçek ilişki yok. `GatewayAuditEntries.Columns` denetim anlık görüntüsü, doğru. Tabloda adı geçmeyen ama aynı sınıfta olanlar: `ground.ProviderBranchId`, `ProviderProjectId`, `PendingSubscriptionCancellations.SubscriptionId`, `StripeWebhookEvents.StripeCustomerId` (dış sistem kimlikleri); `DatabaseExecutorService.cs:132`, `DbHostAccessPolicy.cs:158` (girdi ve yapılandırma ayrıştırıcı). Bir satır eklemek yeter.

### 6. Gerçek uygulama ve performans

Kendi dumanım (`--migrate`, gerçek API, `log_statement=all`): **73/75**. Kalan 2 kontrol B-1'in kanıtı. Gerçek SQL PG logundan alındı; uygulayıcının elle yazdığı şekille aynı.

| Sorgu (200 bin issue, 448 bin bağ, 50 bin etiket) | Süre | Plan |
|---|---|---|
| Etiket COUNT, nadir | 5,5 ms | `IX_issue_labels_ProjectId_NormalizedName` → bağ → `PK_RepoIssues` |
| Etiket COUNT, yaygın | 37,9 ms | Aynı |
| Etiket sayfa, yaygın | 34,4 ms | Aynı, top-N |
| Anahtar doğrulama, 5x5 | 0,16 ms | `IX_GatewayApiKeys_Prefix`, iki çocuk indeks |
| Anahtar doğrulama, 64x64 | 3,5 ms | 4096 satır (L-6) |
| Maske | 0,08 ms | İzin + çocuk indeks |

Tam tarama yok. Log'da `ada@secret.example` ve bağlantı parolası **0**.

### 7. Build ve bağlamlar

Kabul tablosunda.

### 8. Belgelerin dürüstlüğü

İyi: fark tablosu, sapmalar, flake'in "çıkarım" diye yazılması, çalıştırılmayanların (eslint, next build) belirtilmesi. Düzeltilmesi gereken: §1 Security, §5 "geçersiz girdi asla genişletmez", `Phase14_2C_NormalizationScripts.cs:11`, §3 "bağımsız", §3 geri alma komutu, §8'deki komutun sıralılık izlenimi, 110 sn'nin ortam etkisi.

## Backlog (bloklamayan)

| ID | Sev. | Madde | Faz |
|---|---|---|---|
| BL-C1 | MEDIUM | `repo_issue_labels`'a bileşik FK ile proje bütünlüğü | 14.2D-1 |
| BL-C2 | LOW | L-2 karantina saklaması | 14.2D-2 |
| BL-C3 | LOW | L-6 anahtar sorgusu; `RepoIssueLabelStore` Hub'a taşınması | 14.2D-1 |

## Yeniden denetim için kontrol listesi

1. B-1 düzeltmesi, dört yeni değerle gerçek PG testi; Up/Down'da `Restrict*=true`, satır yok.
2. M-1 düzeltmesi ve eşzamanlılık testi.
3. Runbook komutu, 200 bin issue'luk DB'de gerçekten koşmuş.
4. M-3: Prisma/CoderAi yarışı giderildi, tam koşu temiz.
5. Rapor iddiaları düzeltildi; L-1, L-3, L-5 kararı yazıldı.
