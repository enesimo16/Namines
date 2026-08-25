# 07 — Motorlar Arası Dönüşüm (PostgreSQL → MariaDB vb.)

> **Sıra: 4.** Gerçek bir acı, gerçek bir para. Ve altyapının **%70'i zaten var.**

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

## Tipik kayıp noktaları (rapor bunları yakalamalı)

| Kaynak | Hedef | Sorun |
|--------|-------|-------|
| PostgreSQL `enum` | MySQL/MariaDB | Yerel enum var ama farklı semantik |
| PostgreSQL dizi (`text[]`) | Çoğu motor | Karşılığı yok — ayrı tablo ya da JSON |
| `GENERATED ALWAYS AS` | Motor bazlı | Sözdizimi ve kısıtlar farklı |
| `collation` | Motor bazlı | Adlandırma tamamen farklı |
| `SERIAL` / `IDENTITY` | Motor bazlı | Davranış farkı |
| `NUMERIC` hassasiyeti | Motor bazlı | **Sessiz veri kaybı riski** |

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
