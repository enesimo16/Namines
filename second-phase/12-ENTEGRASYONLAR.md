# 12 — Entegrasyonlar (Supabase, Bytebase, Atlas)

> **Sıra: 9.** Doğru içgüdü: bu araçlarla **rekabet etmek değil, üstlerinde
> durmak.**

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

1. Supabase projesine salt-okunur bağlan (bağlantı dizesiyle — introspection
   zaten var)
2. Yerel migration dosyalarını oku (11 numaradaki ayrıştırıcı)
3. Farkı ve riski göster — bu tam olarak bugünkü Change Review ekranı
4. "Uygula" Supabase'in kendi aracına bırakılır

## ⚠️ Dikkat

- **Entegrasyon = bağımlılık.** Karşı taraf API'sini değiştirdiğinde bizim
  özelliğimiz kırılır. Her entegrasyon bir bakım yükü; ikiden fazlasıyla
  aynı anda başlamamalı.
- Salt-okunur başla. Yazma erişimi istemek hem güven hem güvenlik açısından
  çok daha yüksek bir eşik.
- Bytebase ile **entegre olunmaz, farklılaşılır** — aynı işi yapıyorlar.

## 🔴 Yapılmayacak

- Supabase'in yaptığı şeyi yapmaya çalışmak (barındırma, auth, storage).
  Bkz. 14 numara — bu yol ayrı bir şirket demek.
- Kullanıcının üretim veritabanına **yazma** yetkisi almak. Namines kanıtlar
  ve raporlar; uygulamayı kullanıcının kendi aracı yapar. Bu sınır, ürünün
  güven modelinin temeli.
- Aynı anda üç entegrasyon başlatmak. Biri gerçekten çalışsın, sonra ikincisi.
