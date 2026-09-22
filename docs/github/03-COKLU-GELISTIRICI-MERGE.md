# 03 — Çoklu Geliştirici: Branch ↔ DB, Çakışma, Merge

> **Sıra: 3.** Ürünün en ayırt edici parçası. "3-4 kişi aynı anda çalışıyor,
> veritabanı karışıyor" probleminin cevabı burada.

## 1. Problem, tam olarak

Üç kişi aynı depoda çalışıyor:

- Ayşe `users` tablosuna `status varchar(20)` ekliyor.
- Mehmet aynı tabloya `status int` ekliyor (enum olarak düşünüyor).
- Can `users` tablosunu `accounts` olarak yeniden adlandırıyor.

Git bunların **hiçbirini** çakışma olarak görmeyebilir — üçü farklı dosyalarda,
farklı migration'larda olabilir. Merge temiz geçer, `main` bozulur, ve bu
genellikle **üretimde** anlaşılır.

Namines'in elinde git'te olmayan bir şey var: **şemanın anlamı** (`ir.json`) ve
tabloların **kalıcı kimliği** (`StableUuid`). Yeniden adlandırmayı sil+ekle'den
ayıran şey bu — ve semantik merge'i mümkün kılan şey de bu.

## 1b. Bugün var olan parçalar (düzeltme)

Kodu tarayınca çıkan iki şey, bu bölümün kapsamını daraltıyor:

- **Branch başına gerçek DB var** — `IBranchDatabaseProvisioner` ve
  `BranchController`'ın `database` uçları (bkz. [02 §4](02-BOT-VE-PREVIEW-DB.md)).
  Provision / seed / destroy / TTL süpürme / kota hepsi çalışıyor.
- **Çakışma çözme arayüzü var** — `ConflictResolverModal.tsx` +
  `useBranchStore`'daki `MergeConflictItem` ve merge oturumu. Tablo/kolon/ilişki
  bazında çakışmayı yan yana gösterip seçtiriyor.

**Ama sunucu tarafında bir merge motoru YOK.** `MergeConflictItem` listesini
üretecek 3-yollu karşılaştırma bugün yalnızca istemcide; `backend` içinde
`Merge`/`ThreeWay` diye bir servis yok (arandı, bulunamadı).

Yani bu bölümün gerçek işi: **var olan çakışma arayüzünü besleyecek
sunucu-taraflı 3-yollu IR merge motoru** — arayüzü yeniden yazmak değil.

## 2. Eşleme modeli

```
PR / feature branch  →  preview DB     (geçici, TTL'li, PR kapanınca silinir)
main                 →  integration DB (kalıcı, merge kuyruğunun hedefi)
production           →  DOKUNULMAZ     (yalnızca açık onaylı Change Review)
```

Bu üç seviyenin ayrılması pazarlık konusu değil. Preview akışının prod'a
uzanan hiçbir yolu yok.

## 3. Çakışmanın üç sınıfı

Bunlar **farklı problemler** ve karıştırılırsa çözüm de karışır.

### 3.1 Metin çakışması

Aynı dosyanın (`.nsl`, migration) aynı satırları iki branch'te değişmiş. Git
zaten yakalar, ama sunduğu çözüm satır bazlı — şema için anlamsız.

**Namines'in katkısı: IR üzerinde 3-yollu semantik merge.**
`merge-base ↔ A ↔ B` üç şemayı `StableUuid` üzerinden hizalayıp birleştirir:

- A tabloyu yeniden adlandırdı, B ona kolon ekledi → **otomatik birleşir**
  (satır bazlı merge'in çözemeyeceği en yaygın durum).
- İkisi de farklı tablolara dokundu → otomatik birleşir.

### 3.2 Semantik çakışma

Git'in göremediği sınıf. Aynı kavram, uyumsuz tanım:

| Durum | Sınıf |
|---|---|
| A: `status varchar(20)`, B: `status int` | **Uyumsuz** — insan kararı |
| A: `users` → `accounts`, B: `users`'a FK ekliyor | **Karar gerekir** — FK yeni ada bağlanmalı, otomatik düzeltilebilir ama onaylatılır |
| A ve B aynı kolonu aynı tiple ekliyor | **Otomatik birleşir** (idempotent) |
| A tabloyu siliyor, B aynı tabloya kolon ekliyor | **Uyumsuz** |

Çıktı: `SchemaImpactAnalyzer`'ın üçlü diff'i → her fark
`auto | needs-decision | incompatible` olarak sınıflanır ve `/review`
ekranında **yan yana** gösterilir. Kullanıcı her `needs-decision` için seçim
yapar; `incompatible` merge'i bloke eder.

### 3.3 Sıra / versiyon çakışması

İki PR de "migration 48" üretmiş. Klasik ve sinsi: ikisi de kendi branch'inde
doğru, birlikte yanlış.

**Karar: migration versiyonu yazım anında değil, MERGE anında atanır.**
Branch içinde migration'ın kimliği içerik hash'i + `depends_on` (dayandığı
merge-base versiyonu). Merge kuyruğu sıraya girdiğinde sıradaki numarayı alır.

Alternatif (numara yazımda atanıp merge'de rebase edilir) daha tanıdık ama her
merge'de dosya yeniden yazılmasını gerektiriyor — yani yazma izni olmadan
çalışmıyor. Merge anında atama, yazma izni olmayan projelerde de çalışır.

## 4. Merge kuyruğu

```
PR onaylandı
   ↓
sıraya girer (depo başına tek kuyruk)
   ↓
integration DB'de main'in güncel hâline karşı migration uygulanır
   ↓  başarılı                          ↓  başarısız
merge edilir, versiyon atanır      merge bloke, PR'a nedeni yazılır,
                                    integration DB geri alınır
```

Kuyruk **depo başına tek** olmalı: iki PR aynı anda integration DB'ye
uygulanırsa ikisi de kendi başına geçerli, sıraları belirsiz bir sonuç üretir.

`main`'e merge, **prod'a uygulama değildir.** Prod'a gidiş hâlâ `/review`
üzerinden, insan onaylı.

## 5. Canvas farkındalığı (ucuz ve yüksek değerli)

SignalR gerçek zamanlı collab zaten var. Buna eklenecek: canvas'ta bir tablonun
üzerinde **"PR #12'de Mehmet tarafından değiştiriliyor"** rozeti — açık PR'ların
etkilediği tablolardan türetilir.

3-4 kişilik ekipte çakışmaların çoğu, iki kişinin aynı tabloya aynı gün
dokunduğunu **bilmemesinden** çıkıyor. Rozet, çakışmayı çözmüyor; oluşmasını
engelliyor. Maliyeti düşük olan da bu.

## ⚠️ Dikkat

- **Otomatik birleşen fark bile kullanıcıya gösterilir.** Sessiz otomatik
  merge, doğru olduğunda bile güveni bozar — "ne birleştirildi" sorusunun
  cevabı ekranda durmalı.
- **`incompatible` kararını AI vermez.** Sınıflandırma kural motorunun;
  AI yalnızca `ImpactReport`'u insan diline çeviren mevcut Impact Explainer
  rolünde kalır (kendi bulgu üretmez).
- **Integration DB kirlenebilir.** Başarısız bir merge denemesi geri alınmazsa
  sonraki tüm denemeler yanlış tabana karşı çalışır. Her deneme öncesi bilinen
  iyi duruma dönüş (snapshot veya yeniden kurulum) şart.

## 🔴 Yapılmayacak

- **Git'in merge'ini yeniden yazmak.** Metin merge'i git'in işi; biz IR
  seviyesinde ek bir katman sunuyoruz, yerine geçmiyoruz.
- **Çakışmayı "en son yazan kazanır" ile çözmek.** Şemada bu, veri kaybıdır.
  `ReferentialActionSql`'in kuralı burada da geçerli: belirsizlikte **en
  kısıtlayıcı** davranışa düş — yani blokla, otomatik seçme.
- **Prod'a otomatik uygulama.** Merge kuyruğu integration DB'ye kadar.
