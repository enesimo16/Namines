# 11 — Koddan Şema Çıkarma

> **Sıra: 8.** MCP ve GitHub tarafıyla doğal uyum. Var olan tarayıcının
> ters yönü.
>
> ✅ **Yapıldı — iki değil ÜÇ format.** `PrismaSchemaParser`,
> `EfCoreEntityParser` ve `SqlDdlSchemaParser` (ham `CREATE TABLE` — doc'un
> hedef listesinde vardı ve 12 numaranın Supabase akışı buna dayanıyor).
> `CodeSchemaExtractor` formatı tanıyıp yönlendiriyor;
> `POST /api/codeschema/extract` bedava (AI yok, ayrıştırıcılar tamamen
> deterministik). Canvas araç çubuğunda "Schema from Code" paneli dosyaları
> yükleyip sonucu gösteriyor.
>
> **Drift karşılaştırması da çalışıyor** — `compareWith` verilirse çıkarılan
> şema `SchemaImpactAnalyzer` ile canvas'takine karşı karşılaştırılıyor:
> *"kodun şunu diyor, veritabanında şu var"*.
>
> **Canlı doğrulamada gerçek bir hata yakalandı ve düzeltildi:**
> `SchemaImpactAnalyzer` tabloları `StableUuid` ile eşleştiriyor (kendi amacı
> için doğru — yeniden adlandırmayı silmeden ayırmasını sağlayan şey bu), ama
> koddan çıkarılan şemada UUID yok. Hizalama olmadan rapor HER tabloyu
> "silindi + eklendi" gösteriyordu. Birim testler ayrıştırıcıyı ve analizörü
> ayrı ayrı doğruladığı için bunu kaçırmıştı; hata ancak gerçek uca istek
> atılınca göründü. Çözüm: `SchemaUuidAligner` — analizörü DEĞİŞTİRMEDEN,
> karşılaştırma öncesi ad-tabanlı hizalama (analizörü değiştirmek branch
> diff / change review akışlarını bozardı). Regresyon testiyle kilitlendi.

---

## Ne

Bir depoya bakıp içindeki **model/entity tanımlarından** şemayı çıkarmak.
Prisma şeması, EF Core entity'leri, Django modelleri, TypeORM, SQLAlchemy,
Sequelize, ham `CREATE TABLE` dosyaları.

Sonra: çıkan şemayı **canlı veritabanıyla karşılaştırıp** farkı göstermek.
*"Kodun şunu diyor, veritabanında şu var — üç yerde ayrışmışlar."*

## Neden değerli

- Ürün bugün **şemadan koda** gidiyor (18 eject hedefi). Ters yön eksik
- "Affected Code" statik taraması zaten var — dosya tarama altyapısı mevcut
- MCP ile birlikte güçlü: ajan depoda çalışıyor, Namines şemayı çıkarıp
  değişikliğin etkisini söylüyor
- **Kod ile veritabanının ayrışması** yaygın ve sinsi bir problem; migration
  atlanmış, kimse fark etmemiş

## Nasıl

1. ✅ Dosya deseni tanıma → `CodeSchemaExtractor` (`.prisma` > `.cs` > `.sql`)
2. ✅ Format başına ayrıştırıcı — Prisma, EF Core **ve** ham SQL
3. ✅ Çıkan şema → mevcut `DatabaseSchema` modeli
4. ✅ Karşılaştırma: `SchemaImpactAnalyzer` (hizalama sonrası, bkz. yukarıdaki not)

## ⚠️ Dikkat

- **AI ile değil, ayrıştırıcı ile.** ✅ Üç ayrıştırıcı da tamamen
  deterministik; hiçbiri AI'ya gitmiyor. Tanınmayan formatta AI'ya düşülmüyor
  da — uydurmak yerine dürüstçe "tanıyamadım" deniyor (doc'un kendi kuralı:
  "ikisini gerçekten iyi yapmak, sekizini yarım yapmaktan değerli").
- **Kısmi sonuç dürüstçe raporlanmalı.** ✅ `parsedCount` + `skippedCount` +
  her atlananın NEDENİ birlikte dönüyor ve panelde birlikte gösteriliyor.
- Depoyu okumak **izin** ister; GitHub App kapsamı buna göre ayarlanmalı.
  ⏸ Bu kademede depo otomatik taranmıyor — kullanıcı dosyaları kendisi
  seçiyor, yani ek bir izin kapsamı gerekmiyor. GitHub App entegrasyonu
  yapıldığında bu madde yeniden geçerli olacak.
- **Büyük depolarda tarama sınırlandırılmalı.** ✅ 200 dosya / 2 MB; sınırı
  aşanlar atılmıyor, "atlandı" olarak bildiriliyor ve en ilgili dosyalar
  (şema dosyaları) önce alınıyor.

## 🔴 Yapılmayacak

- **Kodu değiştirmek.** ✅ Uyuldu — uç yalnızca metin okur, hiçbir dosya
  yazılmaz.
- Tüm ORM'leri aynı anda desteklemeye çalışmak. ✅ Uyuldu — Django,
  TypeORM, SQLAlchemy, Sequelize **bilerek yapılmadı.** Tanınmayan format
  net bir hata mesajıyla reddediliyor.
- **Migration dosyalarını çalıştırarak şema çıkarmak.** ✅ Uyuldu —
  `SqlDdlSchemaParser` ham metni ayrıştırıyor, hiçbir SQL çalıştırılmıyor ve
  hiçbir veritabanına bağlanılmıyor.
