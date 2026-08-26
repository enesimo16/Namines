# 09 — Şema Alternatifleri (A/B üretim ve karşılaştırma)

> **Sıra: 6.** Fikrin sadeleştirilmiş hâli: çoklu canvas yönetimi yerine
> **var olan diff motorunu** kullanmak.
>
> ✅ **Yapıldı.** Canvas araç çubuğunda "Alternative (~1 round)" butonu —
> yalnızca bir prompt'tan şema üretilmişse aktif (ilk üretimin prompt+cevapları
> `useSchemaStore.lastGenerationPrompt/lastGenerationAnswers`'da tutuluyor).
> Tıklanınca aynı prompt+cevaplarla ikinci bir tur çalıştırılır; sonuç hemen
> uygulanmaz — `AlternativeCompareModal` var olan `calculateSchemaDiff`'i
> (branch diff'te zaten kullanılan motor) A(mevcut)/B(alternatif) için
> çalıştırıp eklenen/silinen/değişen tabloları gösterir. Kullanıcı "Keep
> current (A)" ya da "Use alternative (B)" der; seçilmeyen hiç saklanmadan
> atılır. En fazla iki — doc'un istediği gibi, yığılan bir alternatif listesi
> yok.
>
> **Yeni bir merge/diff UI'ı yazılmadı** — `ConflictResolverModal`'ın kart-ızgara
> görsel dilini taşıyan ama onun branch-merge mantığına bağlı olmayan, ayrı
> ve daha basit bir bileşen (`AlternativeCompareModal.tsx`) yapıldı; doc'un
> kendi tarifine (tablo-tablo seçim değil, tek bir A/B kararı) daha sadık.

---

## Ne

Kullanıcı bir şema üretti ama tam olmadı. "Bir alternatif daha üret" diyor.
İki şema **yan yana diff ekranında** karşılaştırılıyor, biri seçiliyor,
diğeri atılıyor.

## Neden bu şekilde (çoklu canvas yerine)

Orijinal fikir: iki canvas, aralarında dikdörtgen sınırlar, hangisini
sileceğini seçme. Bu, **yeni bir canvas yönetimi katmanı** demek — karışma,
sınır çizimi, kaydetme, hangisi aktif gibi bir sürü yeni durum.

Oysa "iki şemayı karşılaştır ve birini seç" **zaten ürünün çekirdeği**:
`SchemaImpactAnalyzer` ve Database Change Review bunun için yazıldı. Aynı
ekranı kullanmak hem daha ucuz hem daha tutarlı — kullanıcı zaten bildiği bir
arayüzle karşılaşıyor.

## Nasıl

1. Canvas'ta "Alternatif üret" → aynı prompt + cevaplar, farklı bir tur
2. İki şema `SchemaImpactAnalyzer`'a verilir → fark listesi
3. Yan yana gösterim: **A'da olup B'de olmayan**, tip değişiklikleri, ilişki farkları
4. Kullanıcı A'yı ya da B'yi seçer; seçilmeyen atılır

## ⚠️ Dikkat

- **Maliyeti kullanıcı bilmeli.** Alternatif üretmek ikinci bir tur, yani
  ikinci bir token maliyeti. Buton "Alternatif üret (~1 tur)" gibi açık olmalı.
- **En fazla iki.** Üç, dört alternatif karşılaştırma ekranını okunamaz yapar
  ve seçim felcine yol açar.
- Seçilmeyen şema **hemen silinmeli**; "belki lazım olur" diye tutmak,
  kullanıcının hangi şemayla çalıştığını belirsizleştirir — bu, veri kaybından
  daha sinsi bir hata kaynağı.
- Kullanıcı canvas'ta elle değişiklik yaptıysa, alternatif üretmek onu
  **ezecek** — bu durumda uyarı şart. ✅ Uygulandı: alternatif hiçbir zaman
  otomatik uygulanmıyor (kullanıcı A/B ekranında seçim yapana kadar canvas
  değişmiyor) ve modal açıkça "her ikisi de orijinal prompt'tan üretildi,
  o zamandan beri yaptığınız elle değişiklikler alternatife yansımadı" notunu
  gösteriyor.

## 🔴 Yapılmayacak

- Aynı anda birden fazla canvas'ı açık tutmak, aralarında sürükle-bırak.
  Karmaşıklığı yüksek, kazancı düşük.
- Alternatifleri kalıcı "sürüm" olarak saklamak. Sürümleme zaten **branch**
  modelinde var; ikinci bir sürüm kavramı, ikisinin ayrışacağı bir yer daha
  yaratır.
