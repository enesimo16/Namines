# 00 — Neredeyiz

> Faz 1 (`new-phase/`) ne teslim etti ve ürün **bugün** ne yapabiliyor.
> Bu bir plan değil, **durum tespiti** — hepsi yazıldı, test edildi ve çalışan
> bir sisteme karşı doğrulandı.
>
> Kanıt: [../new-phase/CHECKLIST.md](../new-phase/CHECKLIST.md) — G0'dan G52'ye
> her adımın doğrulama kaydı. **1136 test yeşil.**

---

## Sade hâli: ürün ne yapıyor?

Biri Namines'e girip bir cümle yazıyor: *"küçük bir online mağaza, sepet ve
sipariş"*. Sonrasında:

1. **Sistem soru soruyor** — "ürünlerin varyantı olacak mı?", "ödeme takibi
   şemada olsun mu?" gibi en fazla 5 soru. Bu adım **bedava**, AI'ya hiç
   gitmiyor.
2. **Şemayı üretiyor** — cevaplara göre. Ürettikten sonra kendi kendini
   denetliyor: kural motoru + gerçek DDL derleyicisi bakıyor, hata varsa
   düzelttiriyor. Denetleyen AI değil.
3. **Altı veritabanına derliyor** — PostgreSQL, SQL Server, MySQL, MariaDB,
   Oracle, SQLite. Bir motor bir şeyi desteklemiyorsa uyduruk çıktı üretmiyor,
   "bu motorda olmaz" diyor.
4. **18 farklı hedefe kod çıkarıyor** — TypeScript tipleri, Prisma, Drizzle,
   GORM, Zod, GraphQL SDL, EF Core migration, çalışan bir Next.js yönetim
   paneli, şemanın kendi metin biçimi (NSL) ve kanonik JSON.
5. **Canlı veritabanına bağlanıyor** — API üzerinden veri okuyup yazıyor, toplu
   yükleme yapıyor, izin verilirse ham SQL çalıştırıyor.
6. **Değişikliği inceletiyor** — "bu değişiklik veri kaybettirir mi" sorusunu
   cevaplayan bir motor var; riskliyse iki farklı kişinin onayını istiyor.
7. **Ekip olarak çalışıyor** — 3 koltuk, tek kullanımlık davet bağlantısı, ortak
   workspace, "kim ne zaman ne değiştirdi" listesi.
8. **Para alıyor** — Free / Pro 7,5$ / Team 20$. Kod tarafı bitti; Stripe'ta iki
   fiyat kaydı bekliyor.

---

## Katman katman durum

### Şema motoru
- 6 SQL motoru, golden-file testleriyle korunuyor (motor × fixture)
- Gelişmiş alanlar: `identity`, enum, `generated`, `collation`, dizi
- Kanonik JSON IR (`ir.json`) — sürümlü, çift yönlü
- NSL (şemanın kendi metin dili) — parse + yazma, kimlik kararlı

### AI katmanı
- **Netleştirme ajanı:** 14 iş türü tespiti (sıfır token), türe özel en fazla
  5 soru, her sorunun gerekçesi ve varsayılanı
- **Alan uzmanlığı rolleri:** fintech'te para float olmaz, IoT'de ölçüm tablosu
  dar tutulur… türe göre somut kurallar prompt'a giriyor
- **Üret → denetle → düzelt döngüsü:** denetimi kural motoru ve gerçek DDL
  üreticisi yapıyor, ikinci bir model değil
- **3 kendi modelimiz:** NAI v1 Flash (×0,5) / NAI v1 (×1) / NAI v1 Pro (×2).
  Sağlayıcı adı kullanıcıya hiç gösterilmiyor
- **11 gelişmiş ayar** gerçekten okunuyor ve prompt'a uygulanıyor

### Kota ve faturalama
- Plan başına günlük token + paylaşılan havuz, TR gün sınırı
- Sağlayıcı meşgulse 500 değil `Retry-After` ile **429**
- Free / Pro / Team / Enterprise / **Dev** (görünmez, sınırsız, `.env`'den)
- Stripe checkout + webhook + portal; `PlanCode` ile Pro/Team ayrımı

### Ekip ve yetki
- Organizasyon + rol modeli (Viewer / Editor / Admin / Owner)
- Team: 3 koltuk, tek kullanımlık davet linki (SHA-256 özet saklanıyor)
- Ortak workspace — üyeler birbirinin projelerini görüyor
- Change review: risk sınıflandırması, yıkıcı değişiklikte 2 kişi onayı

### Gateway (kullanıcının canlı DB'sine REST)
- `create/update/delete/import/rpc/query/query-nl`
- API anahtarı izin modeli (tablo bazlı + `CanExecuteSql` ayrı)
- Denetim kaydı — Gateway'de tutuluyor, atlatılamaz
- Anahtar başına rate limit (bellek içi — bkz. [02-REDIS-KARARI.md](02-REDIS-KARARI.md))

### Dağıtım yüzeyi
- MCP sunucusu + CLI + Claude Skill
- Namines Bot (GitHub PR yorumu + status check) — **kod hazır, kimlik bekliyor**
- Paylaşım sayfaları, sosyal önizleme, sitemap

---

## Bilerek yapılmayanlar

Bunlar "sonra" listesinde değil, **hayır** listesinde:

- **`docker.sock`'u container'a mount etmek** — host'ta root eşdeğeri yetki verir
- **Bot'un yazım hatası tahmin etmesi** (`aprove` → `approve`) — yıkıcı bir
  değişikliğin yazım hatasıyla onaylanması demek
- **Desteklenmeyen silme davranışında `CASCADE`'e düşmek** — sessiz veri kaybı

Gerekçeler: [../new-phase/32-DEFERRED-NOT-REJECTED.md](../new-phase/32-DEFERRED-NOT-REJECTED.md)

---

## Faz 1'in en pahalı dersleri

| Ders | Nerede öğrenildi |
|------|------------------|
| "Testler geçiyor" hiçbir şey kanıtlamaz | G39 — 857 test yeşilken uygulama hiç başlamıyordu |
| Gerçek motorda çalıştırmadan DDL'e güvenme | G45 — iki hata yalnızca gerçek PostgreSQL/SQLite'ta çıktı |
| Türkçe kültür hatası üretimde olabilir | G44 — `"int".ToUpper()` tr-TR'de `"İNT"` |
| Ayar göstermek onu uygulamak demektir | G52 — 11 ayarın tamamı süstü, kaydediliyor ama okunmuyordu |
| Aynı kuralı iki yere yazma | G49 — kotanın ikinci kopyası token yerine çağrı sayıyordu |
| Kendi kodunu da incele | Üç inceleme geçişinde 12+ bulgu, çoğu bir önceki düzeltmenin doğurduğu |
