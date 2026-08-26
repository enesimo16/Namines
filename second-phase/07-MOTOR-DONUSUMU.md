# 07 — Motorlar Arası Dönüşüm (PostgreSQL → MariaDB vb.)

> **Sıra: 4.** Gerçek bir acı, gerçek bir para. Ve altyapının **%70'i zaten var.**
>
> ✅ **Kayıp raporu + dönüştürülmüş şema + DDL yapıldı.**
> `EngineConversionAnalyzer` (Core) ve `SchemaConverter` (Infrastructure),
> `POST /api/schema/convert/analyze` ve `POST /api/schema/convert/apply` —
> ikisi de `/clarify` ve `/plan` gibi **bedava**, AI'ya hiç gitmiyor. 24 yeni
> test + gerçek MSSQL/Oracle DDL'ine karşı canlı doğrulandı (aşağıda).
>
> **Planlanandan üç sapma, gerekçeli:**
> 1. **Enum burada bir karar noktası DEĞİL.** Araştırırken görüldü ki
>    `EnumSql` zaten karşılığı olmayan motorda enum'u sessizce CHECK kısıtına
>    çeviriyor (kayıpsız) — kullanıcıya sorulacak bir şey yok, tablodaki
>    "yerel enum var ama farklı semantik" satırı yanlış çıktı.
> 2. **`SERIAL`/`IDENTITY` de karar noktası değil** — altı üretici de bunu
>    zaten doğru motor sözdizimine çeviriyor, gerçek bir kayıp yok.
> 3. **`NUMERIC` hassasiyeti raporlanmıyor** — şema modelinde (`SchemaColumn`)
>    precision/scale ayrı alanlar olarak hiç yok, yalnızca tek bir `Length`
>    var. Bu, dönüşümün getirdiği bir kayıp değil, **modelin önceden var olan
>    bir sınırı** — var olmayan bir veriye dayanarak "hassasiyet kaybı" uydurmak
>    yanlış bilgi vermek olurdu. Gerçek kayıp noktaları — dizi, collation,
>    SQLite'ta hesaplanan+PK çakışması — DDL üreticilerinin gerçek motora karşı
>    fırlattığı `NotSupportedException` koşullarıyla bire bir eşleştirilerek
>    kodlandı (bkz. `ColumnFeatureSql`), ayrı bir "yetenek matrisi" icat
>    edilmedi.
>
> ⏸ **4. madde (ephemeral container'da gerçekten çalıştırma) yapılmadı** —
> altyapı (`BranchDatabaseProvisioner`) var ama bu işi convert uçlarına
> bağlamak ayrı bir entegrasyon; şema+DDL üretimi bu oturumun kapsamı.
> Frontend de yok — bu uç şimdilik yalnızca API.

---

## Ne

"PostgreSQL projem var, MariaDB'ye geçmem gerekiyor — neyi kaybederim?"
sorusunu cevaplayan, sonra dönüşümü yapan bir akış.

Üç çıktı:
1. **Kayıp raporu** — hedef motorda karşılığı olmayan her şey, tek tek
2. **Dönüştürülmüş şema** + hedef motorun DDL'i
3. **Veri taşıma notları** — tip daralmaları nerede veri kesebilir

## Neden değerli

Bu, insanların **gerçekten ödediği** bir problem. Ve rakiplerin çoğu tek motor
üzerine kurulu, dolayısıyla cevabı yok.

**Elimizde zaten olan:**
- 6 motor için gerçek DDL üreticisi, golden-file testleriyle korunuyor
- `PortabilityNotes` — bir şemanın diğer motorlarda nerede takıldığı **zaten
  hesaplanıyor**, sadece bir rapor olarak sunulmuyor
- `ReferentialActionSql` — desteklenmeyen davranışta en kısıtlayıcıya düşme kuralı

Yani sıfırdan yazılacak şey az; var olanı birinci sınıf bir özelliğe çevirmek.

## Nasıl

1. `PortabilityNotes`'u **kayıp raporuna** çevir — "not" değil, karar gerektiren
   madde listesi
2. Her madde için üç seçenek sun: **eşdeğerine çevir** / **kısıta düşür** /
   **elle çözeceğim**
3. Kullanıcı kararlarını verir, dönüşmüş şema + DDL üretilir
4. Ephemeral container'da hedef motora karşı **gerçekten çalıştırılır** (altyapı var)

## Gerçek kayıp noktaları (rapor bunları yakalar — ✅ uygulandı)

| Kaynak | Hedef | Sorun | Seçenekler |
|--------|-------|-------|------------|
| PostgreSQL dizi (`text[]`) | PostgreSQL dışı her motor | Karşılığı yok, DDL üretimi reddeder | ayrı tablo / JSON metin / elle |
| `collation` | Oracle | Bu üretici Oracle'a collation hiç yazmıyor | drop / elle |
| `collation` (tireli ad, ör. `tr-TR-x-icu`) | MSSQL/MySQL/MariaDB | Bu motorlar çıplak tanımlayıcı bekler | yaklaşık eşle / drop / elle |
| `GENERATED ALWAYS AS` + PK aynı kolonda | SQLite | SQLite hesaplanan kolonun PK olmasına izin vermiyor | sıradan kolona çevir / elle |

**Karar noktası OLMAYANlar** (araştırmada, üreticinin zaten kayıpsız çözdüğü
görüldü — bkz. yukarıdaki sapma notu): enum, `SERIAL`/`IDENTITY`,
`NUMERIC` hassasiyeti (model bunu hiç taşımıyor).

## ⚠️ Dikkat

- **Sessiz dönüşüm yok.** Karşılığı olmayan her şey kullanıcıya sorulmalı.
  Bu özelliğin tüm değeri "neyi kaybediyorum"u göstermesinde; otomatik
  çevirip susmak, onu sıradan bir dışa aktarıma indirger.
- **Veri taşıma ≠ şema taşıma.** Bu iş şemayı taşır. Veriyi taşımak ayrı ve
  çok daha riskli bir konu — karıştırılmamalı, ayrı bir iş olarak ele alınmalı.
- Tip daralmaları (ör. `numeric(20,10)` → daha dar bir tip) **veri kaybı**
  başlığı altında, kırmızı olarak gösterilmeli.
- Golden-file testleri her yeni dönüşüm kuralında güncellenmeli; `.received`
  dosyasını körlemesine kabul etme.

## 🔴 Yapılmayacak

- **Veri taşımayı otomatik yapmak.** Şema dönüşümü geri alınabilir; yanlış
  taşınmış veri alınamaz. Bu adım en fazla **script üretir**, çalıştırmaz.
- Desteklenmeyen bir özelliği "en yakın" bir şeyle sessizce değiştirmek —
  özellikle `CASCADE` yönünde. Kod tabanının kuralı: varsayılan asla veri
  kaybına doğru düşmez.
