# 12 — Entegrasyonlar (Supabase, Bytebase, Atlas)

> **Sıra: 9.** Doğru içgüdü: bu araçlarla **rekabet etmek değil, üstlerinde
> durmak.**
>
> ✅ **Supabase akışı uçtan uca çalışıyor — ve yeni bir "entegrasyon" YAZILMADAN.**
>
> Araştırmada ortaya çıkan şey: doc'un 4 adımının 3'ü zaten vardı.
> Supabase *Postgres'tir*, dolayısıyla mevcut `DbIntrospectionService`
> herhangi bir Supabase bağlantı dizesiyle çalışıyor ve `table_schema =
> 'public'` filtresi Supabase'in `auth.*`/`storage.*` şemalarını zaten
> dışarıda bırakıyor. Diff + risk ekranı da (`SchemaImpactAnalyzer`, Change
> Review) hazırdı.
>
> **Eksik olan tek halka adım 2'ydi:** "yerel migration dosyalarını oku".
> Supabase migration'ları ham `.sql` dosyaları ve backend'de SQL ayrıştırıcı
> yoktu. `SqlDdlSchemaParser` (bkz. 11 numara) bunu kapattı ve zincir
> tamamlandı — ayrı bir Supabase API entegrasyonu, ayrı bir uç, ayrı bir
> bakım yükü olmadan.
>
> **Canlı doğrulandı:** iki dosyalık bir `supabase/migrations/` klasörü
> (`auth.users` dahil) yüklendi → `auth.users` iç şema olarak dışlanıp
> DÜRÜSTÇE "atlandı" diye bildirildi, `CREATE POLICY` yok sayıldı,
> `character varying(200)` doğru ayrıştırıldı, ve canlı şemada olup
> migration'larda olmayan `avatar_url` kolonu drift olarak raporlandı.
>
> **Bytebase ve Atlas: bilerek yapılmadı.** Doc'un kendi kuralı —
> "ikiden fazlasıyla aynı anda başlamamalı", "Bytebase ile entegre olunmaz,
> farklılaşılır".

---

## Ne

Namines'i var olan yığınların **güvenlik katmanı** olarak konumlandırmak.
Kullanıcı Supabase'i bırakmıyor; Supabase'e giden değişikliği Namines
kanıtlıyor.

## Neden bu doğru yaklaşım

Rakip listesinde bunlar **doğrudan rakip gibi** görünüyor ama değiller:

| Araç | Ne yapıyor | Namines'in yeri |
|------|-----------|-----------------|
| **Supabase** | Barındırma + auth + storage + Postgres | En büyük kullanıcı tabanı. Migration güvenliği zayıf → **en iyi giriş noktası** |
| **Atlas** | Migration'ı kod olarak yönetir, CLI-önce | Görsel inceleme ve teknik olmayan onaylayıcı yok — bizim alanımız |
| **Bytebase** | DB DevOps + review, olgun | En yakın rakip. Entegrasyon değil, farklılaşma gerekir |

**Supabase önceliklidir.** Kalabalık ve büyüyen bir kullanıcı tabanı var,
migration konusunda gerçek bir boşluk var, ve "Supabase migration'ını
göndermeden önce kanıtla" tek cümlede anlaşılıyor.

## Nasıl (Supabase örneği)

1. ✅ Supabase projesine salt-okunur bağlan — `POST /api/dbintrospect`
   (Supabase Postgres olduğu için zaten çalışıyordu; yeni kod gerekmedi)
2. ✅ Yerel migration dosyalarını oku — `SqlDdlSchemaParser`
   (**bu oturumda eklendi**, zincirin eksik halkasıydı)
3. ✅ Farkı ve riski göster — `SchemaImpactAnalyzer` üzerinden
   `POST /api/codeschema/extract` (`compareWith`)
4. ✅ "Uygula" Supabase'in kendi aracına bırakılır — Namines hiçbir
   şey yazmıyor

## ⚠️ Dikkat

- **Entegrasyon = bağımlılık.** ✅ Bu risk fiilen ALINMADI: Supabase'in
  API'sine hiç bağlanılmıyor. Kullanılan tek şey standart Postgres
  protokolü ve düz `.sql` dosyaları — Supabase yarın API'sini değiştirse
  bu akış etkilenmez.
- **Salt-okunur başla.** ✅ Uyuldu — introspection okur, ayrıştırıcı metin
  okur; yazma yolu hiç açılmadı.
- Bytebase ile **entegre olunmaz, farklılaşılır.** ✅ Uyuldu — dokunulmadı.

## 🔴 Yapılmayacak

- Supabase'in yaptığı şeyi yapmaya çalışmak (barındırma, auth, storage).
  Bkz. 14 numara — bu yol ayrı bir şirket demek.
- Kullanıcının üretim veritabanına **yazma** yetkisi almak. Namines kanıtlar
  ve raporlar; uygulamayı kullanıcının kendi aracı yapar. Bu sınır, ürünün
  güven modelinin temeli.
- Aynı anda üç entegrasyon başlatmak. Biri gerçekten çalışsın, sonra ikincisi.
