# 10 — Çoklu Veritabanı Çalışma Alanı

> **Sıra: 7.** Büyük iş ama büyük fark. Pazarda gerçekten boş bir yer.
>
> ✅ **Çekirdek yapıldı, 🔶 "yan yana canvas" görseli bilerek ertelendi.**
>
> Yapılan: `CrossDatabaseRelation` modeli (yeni EF Core tablosu — iki proje
> arasında SourceTable/Column → TargetTable/Column + serbest not), tamamen
> saf `CrossDatabaseImpactAnalyzer` (EF'e hiç dokunmuyor, test edilebilir),
> ve `CrossDatabaseController` (`POST/GET/DELETE /api/crossdatabase/relations`,
> `GET /api/crossdatabase/impact`) — ikisi de projeyi GÖREBİLEN kullanıcıyla
> sınırlı (`OrgAccess.CanViewAsync` iki tarafta da). Canvas'ta yeni bir panel
> (`CrossDatabasePanel.tsx`, araç çubuğunda "Network" ikonu) ilişkileri
> listeler/ekler/siler; her satır **kesikli çerçeve + "Not enforced" etiketiyle**
> gösteriliyor (bkz. aşağıdaki "gerçek FK değil" uyarısı). Bir tablo canvas'tan
> silinince, o tabloya değen kayıtlı ilişkiler varsa kullanıcı toast ile
> uyarılıyor ("N cross-database relation(s) pointing to X").
>
> Gerçek uçtan uca doğrulandı: iki sahte proje (`auth-db`, `orders-db`)
> senkronize edilip aralarında `orders.user_id → auth.users.id` ilişkisi
> kuruldu; `auth-db.users` "siliniyor" sorgusu doğru şekilde
> `{otomatik: orders-db etkilenir}` döndürdü — doc'un kendi örneği birebir.
>
> **Ertelenen kısım:** doc'un tam tarifi olan "yan yana iki canlı React Flow
> canvas'ı, aynı ekranda" — bu, kendi başına ayrı bir UI katmanı (çoklu
> canvas render/senkron/performans) ve bu oturumun kapsamına sığmadı. Onun
> yerine ürünün asıl iddiasını taşıyan kısım önceliklendirildi: ilişkiyi
> KAYDETMEK ve kırılmayı ÖNCEDEN GÖSTERMEK — doc'un kendi "neden değerli"
> bölümünün tam olarak söylediği şey. Şu an her proje kendi canvas'ında
> açılıyor; CrossDatabasePanel üzerinden karşı projenin şemasına (salt-okunur,
> tablo/kolon seçimi için) erişiliyor ama ikisi aynı ekranda render edilmiyor.

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

1. ~~**Workspace** kavramı~~ — ayrı bir entity açılmadı; ilişki doğrudan iki
   `CloudProject.Id`'sini taşıyor, "hangi projeler birbirine bağlı" sorgusu
   ilişkilerden türetiliyor (`CrossDatabaseImpactAnalyzer.LinkedProjectIds`).
   Organizasyon zaten yetki sınırı olduğu için ayrı bir üst katmana gerek
   çıkmadı.
2. 🔶 Yan yana canvas — **yapılmadı**, aşağıda gerekçesi var.
3. ✅ **Mantıksal ilişki** tanımı — `CrossDatabaseRelation` tablosu, tam da
   burada tarif edildiği gibi: veritabanı zorlamıyor, biz kaydediyor ve
   kontrol ediyoruz.
4. ✅ Etki analizi bu bağları tarıyor — `GET /api/crossdatabase/impact`,
   doc'un kendi örneğiyle (`auth-db.users` siliniyor → `orders-db` etkilenir)
   uçtan uca doğrulandı.

## ⚠️ Dikkat

- **Mantıksal ilişki, gerçek FK değil.** ✅ Ele alındı: her ilişki arayüzde
  kesikli çerçeve + "Not enforced" etiketiyle gösteriliyor.
- **Performans:** üç canvas aynı anda React Flow demek. ⏸ Henüz gündemde
  değil — yan yana canvas yapılmadığı için bu risk şu an yok; o iş
  başladığında yeniden değerlendirilmeli.
- Bu iş, kota ve plan tarafını da etkiler — kaç DB, hangi planda? ⏸ Cevaplanmadı,
  fiyatlandırma bu oturumun kapsamı dışında.
- Önce **iki** DB ile başla. ✅ Uygulama da bunu izledi: canlı doğrulama iki
  proje (`auth-db`, `orders-db`) ile yapıldı, üçüncüyle test edilmedi.

## 🔴 Yapılmayacak

- Veritabanları arası **gerçek** yabancı anahtar üretmeye çalışmak. Çoğu motor
  bunu desteklemez; destekleyende de (aynı sunucu içinde) dağıtık sistem
  tasarımına aykırıdır.
- Çapraz veritabanı **JOIN** üretmek. Mantıksal ilişkiyi göstermek başka,
  onu sorguya çevirmek başka — ikincisi sessizce çalışmayan SQL üretir.
- Çoklu DB'yi tek bir birleşik şemaya "düzleştirmek". Sınırlar bilinçli
  konmuş; onları silmek kullanıcının mimarisini bozar.
