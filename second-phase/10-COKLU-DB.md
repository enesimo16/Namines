# 10 — Çoklu Veritabanı Çalışma Alanı

> **Sıra: 7.** Büyük iş ama büyük fark. Pazarda gerçekten boş bir yer.

---

## Ne

Bir çalışma alanında **birden çok veritabanı** yan yana. Her biri kendi
canvas'ında, ama aralarındaki ilişkiler ve riskler görünür.

Tipik durum: mikroservisler. `auth-db`, `orders-db`, `billing-db`. Üçü de
`user_id` taşıyor ama aralarında **yabancı anahtar yok** — çünkü ayrı
veritabanları. Kimse bu bağı göremiyor, ta ki biri `auth-db.users`'ı silene
kadar.

## Neden değerli

- Gerçek ve yaygın bir acı; mikroservis kullanan herkeste var
- **Kimse çözmüyor** — ERD araçları tek veritabanı varsayıyor
- Etki analizi motoru (`SchemaImpactAnalyzer`) zaten var; onu veritabanı
  sınırının **ötesine** taşımak doğal bir genişleme
- "Veritabanları arası kırılma" analizi, ürünün çekirdek vaadinin en güçlü hâli

## Nasıl

1. **Workspace** kavramı: birden çok projeyi/DB'yi bir arada tutan üst katman
   (organizasyon modeli zaten var, üstüne kurulabilir)
2. Yan yana canvas — her DB kendi sınırında
3. **Mantıksal ilişki** tanımı: `orders-db.orders.user_id` → `auth-db.users.id`.
   Veritabanı bunu zorlamıyor, biz **kaydediyoruz ve kontrol ediyoruz**
4. Etki analizi bu bağları da tarasın: "`auth-db.users` siliniyor →
   `orders-db` ve `billing-db` etkilenir"

## ⚠️ Dikkat

- **Mantıksal ilişki, gerçek FK değil.** Veritabanı bunu doğrulamaz; bizim
  kaydımızdır. Arayüzde farklı gösterilmeli (kesik çizgi vb.), yoksa kullanıcı
  veritabanının koruduğunu sanır — bu, olmayan bir güvenlik hissi yaratır ve
  tam da önlemeye çalıştığımız hatayı üretir.
- **Performans:** üç canvas aynı anda React Flow demek. Node sayısı büyüdükçe
  ağırlaşır; görünürlük bazlı yükleme gerekebilir.
- Bu iş, kota ve plan tarafını da etkiler — kaç DB, hangi planda?
- Önce **iki** DB ile başla. Üç ve fazlası, iki çalışmadan tasarlanmamalı.

## 🔴 Yapılmayacak

- Veritabanları arası **gerçek** yabancı anahtar üretmeye çalışmak. Çoğu motor
  bunu desteklemez; destekleyende de (aynı sunucu içinde) dağıtık sistem
  tasarımına aykırıdır.
- Çapraz veritabanı **JOIN** üretmek. Mantıksal ilişkiyi göstermek başka,
  onu sorguya çevirmek başka — ikincisi sessizce çalışmayan SQL üretir.
- Çoklu DB'yi tek bir birleşik şemaya "düzleştirmek". Sınırlar bilinçli
  konmuş; onları silmek kullanıcının mimarisini bozar.
