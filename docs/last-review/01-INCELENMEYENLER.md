# İncelenmeyen alanlar — bir sonraki turun iş listesi

> **Bu listedeki hiçbir şey "temiz" değil. Bakılmadı.**
> Bir alanın burada olması, orada sorun olmadığı anlamına gelmez; kimsenin
> oraya bakmadığı anlamına gelir. Bulunan sorunlar için [02-BULUNAN-HATALAR.md](02-BULUNAN-HATALAR.md).

Sıra, tahmini risk × erişilebilirlik ile belirlendi.

---

## Bu tur ne değiştirdi (2026-10-01/02)

Düzeltme turunda bazı alanlara **kısmen** bakıldı. Aşağıdaki notlar neyin
gerçekten incelendiğini sınırlıyor — "düzeltme yaptım" ile "alanı denetledim"
aynı şey değil ve bu dosyanın tüm değeri o ayrımda.

| # | Durum |
|---|---|
| **1. DDL üreticileri** | **Yalnızca TANIMLAYICI ALINTILAMASI** incelendi ve düzeltildi (C-1): 43 nokta `SqlIdentifier`'a yönlendirildi, kontrol karakterleri reddediliyor, `COLLATE` ve `CREATE TYPE` dahil. Listedeki **diğer sorular hâlâ açık**: `DefaultValueSql` ifadeleri, check/generated kolon ifadeleri, `COMMENT ON`, rezerve kelimeler, Unicode tanımlayıcılar. `EnumSql`'in DEĞER alıntılaması (tek tırnak) okundu ve doğru çıktı — bkz. 03. |
| **2. Cache katmanı** | **Dokunulmadı.** Tek ilgili değişiklik, CoderAI'ın semantik önbellek anahtarına `promptVersion` eklenmesi (M-17) — kiracı bağlamı sorusuna cevap değil. **Bir sonraki turun 1 numarası bu olmalı.** |
| **5. `MySqlFamilyBackupProvider`** | **Kısmen:** bağlantı hedefinin çözümlenmesi incelendi ve düzeltildi (M-12 — ad yerine doğrulanan IP geçiliyor). **Komut kurma yüzeyi** (kabuk çıktığı argümanlar, `Quote`) incelenmedi. |
| **3, 4, 6, 7, 8, 9** | **Dokunulmadı.** |

> Bu turda 1 CRITICAL, 7 HIGH, 17 MEDIUM ve 5 LOW kapatıldı (bkz.
> [02-BULUNAN-HATALAR.md](02-BULUNAN-HATALAR.md)) ve düzeltmelerin kendisi üç
> regresyon üretti. Bu, bu listedeki alanların daha az önemli olduğu anlamına
> gelmiyor — tersine: bulunan her şey BAKILAN yerlerden geldi.

---

## 1. DDL üreticilerinin enjeksiyon dışındaki yüzeyi 🔴

**Neden en üstte:** Bu turun tek CRITICAL bulgusu (tanımlayıcı kaçışı) tam olarak
burada çıktı ve denetçi bu dosyalara geçerken durdu. Aynı altı dosyada başka ne
olduğu **bilinmiyor**.

**Dosyalar:** `backend/Namines.Infrastructure/Generators/DdlGenerator/` altındaki
altı motor üreticisi + `TypeSql.cs`, `DefaultValueSql.cs`, `ReferentialActionSql.cs`,
`EnumSql.cs`, `IndexSql.cs` (varsa).

**Sorulacaklar:**
- Varsayılan değer ifadeleri (`DefaultValueSql`) — kullanıcı metni SQL'e ham mı giriyor?
- Check constraint ifadeleri, generated/computed kolon ifadeleri, collation adları
- Enum değerleri (`EnumSql`) — tırnak kaçışı
- Kolon/tablo yorumları (`COMMENT ON`)
- Motor bir özelliği desteklemiyorsa **en kısıtlayıcıya mı düşüyor** yoksa sessizce
  CASCADE gibi yıkıcı bir davranışa mı? (`ReferentialActionSql` bu prensibi
  belgeliyor — diğer alanlarda da tutuluyor mu?)
- Rezerve kelimeler, büyük/küçük harf duyarlılığı, şema niteleme, Unicode tanımlayıcılar

---

## 2. Cache katmanı — kiracı bağlamı 🔴

**Neden yüksek:** Hiç denetlenmedi. Bir cache anahtarında org/kullanıcı bağlamının
eksik olması, klasik ve sessiz bir çapraz-kiracı sızıntısıdır.

**Dosyalar:** `IMemoryCache` kullanan her yer (`Program.cs:530` kayıtlı),
Redis anahtar isimlendirmesi (`Program.cs:457-464` backplane).

**Sorulacaklar:**
- Her cache anahtarında `orgId`/`userId`/`projectId` var mı? (`project:{id}` yerine
  `org:{orgId}:project:{id}`)
- Yetki/rol/kota değeri cache'leniyor mu? Cache'leniyorsa iptal sonrası ne kadar
  bayat kalıyor?
- Gateway API anahtarı doğrulaması cache'lenmiyor (bu **doğrulandı**, bkz. 03) —
  başka hangi güvenlik kararı cache'leniyor?

---

## 3. Uç başına negatif matris 🟡

Her yüksek riskli uç için sistematik olarak denenmedi:
`{yanlış org, yanlış proje, çıkarılmış üye, Viewer/Editor/Admin/Owner/Billing,
süresi dolmuş jeton, iptal edilmiş jeton, kapsamı yetersiz anahtar,
başka projeye verilmiş anahtar, eşzamanlı istek, tekrar edilen istek}`

**Hedef uçlar:** Desk SQL, Gateway `/query` `/export` `/import` `/rpc`,
Vault restore + download, Ground provision + delete, Launch + download,
anahtar create/revoke, üye add/change/remove, share create/revoke,
CanvasHub join + update.

---

## 4. `SqlWorkbenchService` ve `DbPrivilegeInspector` 🟡

İkisi de SQL çalıştıran/veritabanı yetkisi okuyan yollar; denetçi B'nin listesinde
vardı, ulaşamadı.

---

## 5. `MySqlFamilyBackupProvider` komut kurma yüzeyi 🟡

Denetçi D, `ContainerBackupProvider` ve Postgres sağlayıcısını okudu; MySQL
sağlayıcısının `sh -c` dizeleri **satır satır okunmadı**. CRITICAL-1'in aynı
sınıfı (kabuk dizesine ham değer) burada da olabilir.

> Not: `MySqlFamilyBackupProvider.cs:113-130`'daki `Quote` yardımcısı tırnak
> kaçışı yapıyor (`'\''`) — bu kısım denetlendi ve doğru. Kalan yüzey okunmadı.

---

## 6. 19 eject üreticisi — zip entry adları 🟡

`CompileController` ve `ScaffolderService` arşiv girdilerini üretici çıktısından
kuruyor; bazıları tablo adından türüyor. Bir tanımlayıcı yol parçasına
kaçışsız giriyorsa arşiv "zip slip" taşıyan bir girdi içerebilir.

**Sınırlı etki:** Paketi çıkaran kullanıcı kendisi; yine de ortak bir
`SafeEntryName` yardımcısı doğru olur.

---

## 7. `SchemaAgentPipeline` çok turlu döngüsü 🟢

Agent tool yüzeyi denetlendi ve **yetkisiz** çıktı (bkz. 03). Denetlenmeyen:
çok turlu döngünün kota/retry davranışı ve deterministik kapının kısa devre
edilip edilemeyeceği.

---

## 8. Desk sayfaları ve frontend sunucu-taraflı kod 🟢

Denetlenen: `handoff/route.ts` (tam), `lib/auth.ts`, `lib/rich.ts`, XSS sink taraması.
Denetlenmeyen: Desk'in diğer sayfaları, `frontend/` içindeki route handler /
server action / middleware varsa onların dışa giden çağrıları.

---

## 9. Diğer küçük boşluklar 🟢

- `AIQuotaMiddleware`'in rezervasyon/tüketim muhasebesi (yol eşleştirmesi denetlendi)
- `AutomationRunLogRetentionService`, `DockerSweeperBackgroundService` gövdeleri
- `NeonClient` / `SupabaseClient` HTTP katmanı (jeton işleme, yanıt güveni)
- `QuotaController` / `UsageController` gövdeleri
- `Namines.Mcp` araçlarının kota tüketimi
- `CoderAIPackagerService`'in `Outputs/` altına dosya yazması
- EF transaction kullanımı (change-request/branch/kota/webhook dışındaki yollar)
