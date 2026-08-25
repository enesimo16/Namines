# 11 — Koddan Şema Çıkarma

> **Sıra: 8.** MCP ve GitHub tarafıyla doğal uyum. Var olan tarayıcının
> ters yönü.

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

1. Dosya deseni tanıma → hangi ORM/format
2. Format başına ayrıştırıcı. **Önce iki tane**: Prisma ve EF Core
   (Prisma en yaygın, EF Core bu kod tabanının zaten bildiği şey)
3. Çıkan şema → mevcut `DatabaseSchema` modeli
4. Karşılaştırma: `SchemaImpactAnalyzer` ile canlı DB'ye karşı

## ⚠️ Dikkat

- **AI ile değil, ayrıştırıcı ile.** Prisma şeması yapılandırılmış bir dosya;
  onu modele okutmak hem pahalı hem güvenilmez. AI yalnızca **tanınmayan**
  formatlarda son çare olmalı.
- Kısmi sonuç dürüstçe raporlanmalı: "12 modelin 9'u okundu, 3'ü anlaşılamadı"
  — eksik olanı sessizce atlamak, olmayan bir tam resim sunar.
- Depoyu okumak **izin** ister; GitHub App kapsamı buna göre ayarlanmalı.
- Büyük depolarda tarama sınırlandırılmalı (dosya sayısı/boyut).

## 🔴 Yapılmayacak

- **Kodu değiştirmek.** Bu özellik okur ve raporlar. Kod üretmek/düzeltmek
  ayrı bir iş ve bir ajanın işi — Namines'in değil.
- Tüm ORM'leri aynı anda desteklemeye çalışmak. İki tanesini gerçekten iyi
  yapmak, sekizini yarım yapmaktan değerli.
- Migration dosyalarını çalıştırarak şema çıkarmak. Rastgele bir depodan
  gelen migration'ı çalıştırmak kod çalıştırmaktır — güvenlik açığıdır.
