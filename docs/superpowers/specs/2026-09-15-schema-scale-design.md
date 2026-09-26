# Büyük Şema Üretimi (50-60 Tablo) — Spec

**Durum:** Onaylandı (üç mimari karar kullanıcı tarafından seçildi) — uygulama planı bekleniyor.

> ✅ **Bitmiştir.** Plan yazıldı ([`../plans/2026-09-15-schema-scale-plan.md`](../plans/2026-09-15-schema-scale-plan.md))
> ve dokuz görevin tamamı uygulandı: `SchemaScopePlan`, `SchemaScopePartitioner`,
> `SchemaChunkMerger`, `AiOutputTruncatedException`, `ISchemaDraftSource.DraftChunkAsync`,
> hattın parçalı yola bağlanması ve kapsam-farkında onarım.
> (2026-09-26'da koddan doğrulandı.)

**Sorun:** "Kapsamlı bir proje" istendiğinde motor 5-6 tablo üretiyor. Kullanıcı
Pro planda, Advanced açık, istek metni açıkça kapsamlı — yine de küçük bir şema
geliyor. Hedef: istendiğinde 50-60 tablolu şema üretebilmek.

**Kök neden ÜÇ ayrı yerde, hiçbiri prompt'un "yetersiz" olması değil:**

1. **Plan turu kapsamı 12 satıra kilitliyor.**
   `AgentPlanPromptBuilder.BuildSystemPrompt()`: *"List, in at most 12 lines"* —
   ve o 12 satır tablolar + ilişkiler + computed kolonlar + index'ler +
   unique/check + trigger + saklı yordamları birlikte kapsayacak. 50 tablolu bir
   istekte plan 8-10 tablodan fazlasını sayamaz. `DraftAsync` ardından
   `"Follow this plan when creating the schema"` diyerek planı dayatıyor.
   Plan turunun kendi doc yorumu amacını "modelin büyük isteklerde tablo
   atlamasını önlemek" diye tanımlıyor; bugün tablo atlamanın birincil sebebi o.

2. **Tek çağrıda 50-60 tablo çıktı token'ına sığmıyor.**
   Ölçüm (bu kod tabanının gerçek JSON formatından): kolon ≈ 50 token, 8 kolonlu
   + 2 index'li tablo ≈ 550 token, ilişki ≈ 45 token.

   | Tablo | Gereken çıktı | Tavan |
   |---|---|---|
   | 6 | ~3.500 | Free 6.000 |
   | 30 | ~19.000 | Pro 16.000 ❌ |
   | 50 | ~32.000 | Team 32.000 (sınırda) |
   | 60 | ~40.000 | hiçbir katman ❌ |

   Ayrıca `llama-3.3-70b-versatile`'ın sağlayıcı tamamlama sınırı 32.768.
   **Parçalama bir optimizasyon değil, zorunluluk.**

3. **Kesilme görünmez.** `finish_reason` kod tabanının hiçbir yerinde okunmuyor
   (`grep -rn finish_reason --include=*.cs` → sıfır sonuç). Model tavana çarpıp
   JSON'u kesince `JsonException` fırlıyor ve `GenerateSchemaAsync`
   **sıcaklığı artırarak** (`temperature + attempt * 0.2`) aynı `max_tokens` ile
   yeniden deniyor — uzunluk hatasına sıcaklık artırmak modeli daha da dağıtıp
   yine kestiriyor. Kullanıcıya dönen mesaj (*"Failed to generate a valid JSON
   schema after 2 retries"*) asıl sebebi hiç söylemiyor.

**Bonus kök neden (4):** onarım turunun fallback yolu `ReviseSchemaAsync`,
tavanı `CalculateMaxTokens()` ile katmandan bağımsız olarak **6.000 token**'a
düşürüyor; `RepairAsync` ise modele TÜM tabloları gönderip hepsini geri
bekliyor. 40 tablolu bir şemada bu sessiz bir tablo öğütücüsü.

**Kapsam dışı (ayrı iş):** `AiAdvancedSettings.MaxTokens` varsayılanının
"4096" olması (her katmanı planının tavanının altına kilitliyordu) bu spec
yazılmadan önce ayrı bir commit'te düzeltildi — burada tekrar ele alınmıyor.

---

## Mimari: alan-bölmeli (domain-partitioned) üretim

```
Tur 1  PLAN         → YAPISAL JSON (satır sınırı yok, yalnızca isimler ≈ 1.500 token)
        │
        ├─ toplam tablo ≤ 12  → bugünkü TEK taslak çağrısı (değişiklik yok)
        │
        └─ toplam tablo > 12  → alanlar ~8-10 tablo hedefiyle GRUPLANIR
                                 her grup KENDİ çağrısında, PARALEL
                                 ↓
                               MERGE (isimle tekilleştirme + FK id çözümleme)
                                 ↓
Denetim → onarım               mevcut döngü, birleşmiş bütün üzerinde AYNEN
```

### Bölüm 1: Yapısal plan

`AgentPlanPromptBuilder` düz metin yerine JSON üretir:

```json
{
  "schemaName": "string",
  "domains": [
    { "name": "Identity & Access",
      "tables": ["users", "roles", "permissions", "user_roles", "sessions"] },
    { "name": "Catalog",
      "tables": ["products", "categories", "product_categories", "variants"] }
  ]
}
```

**Yalnızca İSİMLER** — kolon/tip/kısıt yok. 60 tablo için bile ~1.500 token,
yani plan turu bugünkünden ucuz kalıyor. Satır sınırı KALDIRILIYOR; yerine
kapsam talimatı geliyor: istek kapsamlı/kurumsal bir sistem tarif ediyorsa
alan başına eksiksiz tablo listesi çıkar, asgari bir alt küme değil.

Plan ayrıştırılamazsa (`null` veya bozuk JSON) hat bugünkü davranışını
koruyor: plansız, tek taslak çağrısı. Plan bir İYİLEŞTİRME, zorunluluk değil —
`SchemaAgentPipeline`'ın mevcut sözü bozulmuyor.

Tek-çağrı yoluna giden durumda plan JSON'u düz metne render edilip bugünkü
gibi prompt'un başına ekleniyor.

### Bölüm 2: Deterministik kimlik sözleşmesi

Parçalar birbirinin ürettiği id'leri göremez. Alanlar arası yabancı anahtarın
çalışması için kimlikler **isimden türetilir** ve bu her parça çağrısına kural
olarak yazılır:

- tablo id'si: `t_<snake_case_tablo_adı>`
- kolon id'si: `c_<snake_case_tablo_adı>_<snake_case_kolon_adı>`

Model bu sözleşmeyi tutturamazsa merge adımı **isimle geri çözüyor** (bkz.
Bölüm 4) — sözleşme birinci savunma, isim çözümlemesi ikinci.

### Bölüm 3: Parça çağrıları

Alanlar, çağrı başına ~8-10 tablo hedefiyle gruplanır (3 tablolu bir alan tek
başına çağrı hak etmiyor; 20 tablolu bir alan bölünür). Her çağrı:

- **TANIMLAYACAĞI** tabloların listesini alır,
- şemadaki **DİĞER TÜM** tabloların isimlerini bağlam olarak alır,
- "yalnızca senin tablolarını tanımla; diğerlerine yabancı anahtarla
  başvurabilirsin ama onları TANIMLAMA" talimatını alır.

Çağrılar paraleldir. Biri patlarsa diğerleri iptal edilmez: eldeki parçalar
birleştirilir ve eksik alan bir not olarak raporlanır — 60 tablonun 50'sini
vermek, hiçbir şey vermemekten iyidir. (Hattın mevcut "elde kalan şema — hatalı
olsa bile döner" sözüyle aynı ilke.)

### Bölüm 4: Merge

1. Tüm parçaların tabloları birleştirilir, **normalize edilmiş isme göre**
   tekilleştirilir (ilk kazanır; tekrar edenler not olarak raporlanır).
2. İlişkiler birleştirilir. Bir `sourceTableId`/`targetTableId` gerçek bir tablo
   id'siyle eşleşmiyorsa, **isimle** (veya `t_<isim>` formuyla) aranır ve
   bulunursa yeniden yazılır.
3. Hâlâ çözülemeyen ilişki (hedef tablo hiç üretilmemiş) **düşürülür** ve not
   olarak raporlanır. Denetim döngüsü ardından yine çalışır; deterministik
   olarak çözülebilen bir şeyi modele onarım turu harcatmak yanlış olurdu.

### Bölüm 5: Kapsam-farkında onarım

Bugün `RepairAsync` şemanın TAMAMINI gönderip tamamını geri bekliyor ve
fallback yolu 6.000 token'a sıkışıyor. Değişiklik:

- Bulgu metinlerinden **etkilenen tablo adları** çıkarılır.
- Modele yalnızca o tablolar + diğer tabloların **isim listesi** gönderilir.
- Dönen alt küme ana şemaya isimle **yamalanır**
  (`SchemaMerge.SpliceTables`).
- Hiçbir tablo adı çıkarılamayan şema düzeyi bulgularda bugünkü davranış
  korunur (tamamı gönderilir).
- `CalculateMaxTokens` sabit 4096/6000 yerine **katman tavanını** kullanır.

Böylece onarım maliyeti şema büyüklüğünden bağımsızlaşır.

### Bölüm 6: Kesilmeyi görünür kılmak

- `finish_reason == "length"` okunur ve **ayrı bir istisna** (`AiOutputTruncatedException`)
  olarak yükseltilir.
- Bu istisnada **sıcaklık artırılarak yeniden denenmez** — uzunluk hatasının
  çaresi sıcaklık değil. Mesaj kullanıcıya gerçek sebebi söyler: çıktı token
  tavanına ulaşıldı, kapsamı daraltın ya da planınızın tavanını yükseltin.
- Parçalı üretimde bir parça kesilirse o parça bir not üretir; diğerleri
  etkilenmez.

### Bölüm 7: Kapsam-farkında kota

- Rezervasyon iki aşamalı olur: plan turu için küçük bir rezervasyon, plan
  dönüp tablo sayısı bilindikten sonra kalan iş için ikinci rezervasyon.
- `AgentQuotaReservation.TokensForScope(tableCount, repairRounds)` ≈
  `tableCount × 600` (taslak) + onarım turları.
- İkinci rezervasyon reddedilirse üretim **başlamadan** temiz bir SSE `error`
  olayıyla durur ve ne kadar gerektiğini/ne kadar kaldığını söyler — yarıda
  kesilmiş bir şema vermez.

---

## Uygulama Sonrası Düzeltme — eşik ve parça boyutu KATMANA BAĞLI

Yukarıdaki `12` eşiği ve `~8-10` parça hedefi bu spec'te **sabit sayı** olarak
yazıldı; oysa hemen üstlerindeki tablo bu sayıları katman tavanından
hesaplıyordu (Free 6.000 / Pro 16.000 / Team 32.000, ~600 token/tablo).
Bütünsel branş incelemesi bu tutarsızlığı yakaladı: sabit `12` eşiğiyle, Free
katmandaki bir kullanıcının 11-12 tablolu planı tek çağrı yoluna düşüyor,
~6.600 token gerektirip 6.000 tavanına çarpıyor ve **her seferinde kesiliyor**
— üstelik tam da o durumu çözecek parçalama mekanizması hemen yanı başında
kullanılmadan duruyor.

Bu bir uygulama hatası değil, **bu spec'in hatasıydı**: tavan tablosundan
hesaplayıp ardından sonucu sabitledi.

Düzeltilmiş kural: hem eşik hem parça hedefi etkin çıktı tavanından türetilir
(`tavan / 600`), bugünkü sabitler ÜST SINIR olarak korunur. Böylece Pro ve
Team davranışı birebir aynı kalır (16.000/32.000 → 26/53 hesaplanır, 12/9'a
kırpılır), yalnızca dar tavanlı katmanlar daha küçük parçalar alır. Tavan
bilinmiyorsa eski sabitlere düşülür.

## Test stratejisi

- **Plan ayrıştırma:** geçerli JSON, bozuk JSON, boş domain listesi, eşik
  altı/üstü tablo sayısı — saf fonksiyon, AI'sız birim testleri.
- **Gruplama:** alanların ~8-10 tabloluk çağrılara bölünmesi deterministik ve
  test edilebilir olmalı (3 tablolu alan tek başına çağrı almamalı; 25 tablolu
  alan bölünmeli).
- **Merge:** isimle tekilleştirme, `t_<isim>` id çözümlemesi, çözülemeyen
  ilişkinin düşürülmesi ve not üretmesi.
- **Onarım kapsamı:** bulgu metninden tablo adı çıkarma; `SpliceTables`'ın
  yalnızca adı geçen tabloları değiştirip diğerlerini (ve trigger/enum/SP'yi)
  koruması.
- **Kesilme:** `finish_reason: "length"` içeren sahte bir yanıtın
  `AiOutputTruncatedException` üretmesi ve sıcaklık artırarak yeniden
  DENENMEMESİ.
- **Parça hatası dayanıklılığı:** bir parça çağrısı patladığında diğerlerinin
  sonucunun yine döndüğü.

## Kapsam Dışı (v1)

- Parça çağrılarının kendi içinde ayrı onarım turu alması (onarım yalnızca
  birleşmiş bütün üzerinde).
- Kullanıcının alan listesini üretimden önce görüp düzenlemesi (plan ekranı
  zaten var; bunu ona bağlamak ayrı bir iş).
- 60'tan çok daha büyük şemalar için ikinci seviye parçalama.
