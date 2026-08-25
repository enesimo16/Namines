# 13 — Dağıtım Hedefleri (Plesk, cPanel, mobil/SQLite)

> **Sıra: 10.** İlk değerlendirmemde "niş" demiştim — **haksızdım.** Aşağıda
> neden fikrimi değiştirdiğim yazıyor.

---

## Ne

Üretilen şemayı, **komut satırı ve Docker olmayan** ortamlara sokulabilir
hâle getirmek:

- **Plesk / cPanel / DirectAdmin** — phpMyAdmin ile içe aktarılabilir SQL
- **Paylaşımlı barındırma** — tek dosya, bağımlılık yok
- **Mobil (SQLite)** — uygulama içine gömülecek hazır `.db` dosyası + migration

## Neden fikrimi değiştirdim

İlk bakışta "niş" görünüyor çünkü geliştirici araçları dünyası `psql`, Docker
ve CI varsayıyor. Ama:

- **Küçük ajanslar ve serbest çalışanların büyük kısmı paylaşımlı barındırma
  kullanıyor.** Türkiye ve benzeri pazarlarda bu oran daha da yüksek
- O ortamlarda **Docker yok, CLI yok, migration aracı yok** — elinde
  phpMyAdmin ve bir SQL kutusu var
- Rakiplerin **hiçbiri** bu kitleye bakmıyor; hepsi CI/CD varsayıyor
- Ve bu kitle §1.5'teki "ödeme isteği yüksek" segmentin ta kendisi:
  **DBA'sı olmayan ama production'ı olan** ekipler

Yani niş değil — **rakiplerin görmezden geldiği bir kitle.** Farkı da bu.

## Nasıl

Bu büyük ölçüde **çıktı biçimlendirme** işi; yeni motor yazmak değil.

1. **phpMyAdmin uyumlu SQL** — tek dosya, `SET FOREIGN_KEY_CHECKS`, doğru
   sıralama, satır uzunluğu sınırı (bazı panellerde yükleme limiti var)
2. **Yükleme boyutu bölme** — panellerin çoğunda dosya boyut sınırı var;
   şema büyükse parçalara ayır
3. **Adım adım talimat** — "Plesk → Veritabanları → İçe Aktar → şu dosyayı seç"
4. **SQLite mobil paketi** — hazır `.db` + platform başına migration örneği

## ⚠️ Dikkat

- **Karakter seti tuzağı.** Paylaşımlı barındırmada varsayılan çoğu zaman hâlâ
  `latin1` ya da `utf8` (gerçek UTF-8 değil). Türkçe karakterler burada sessizce
  bozulur. Üretilen SQL `utf8mb4`'ü **açıkça** belirtmeli.
- **MySQL sürümü eski olabilir.** Paylaşımlı barındırmada MySQL 5.7 hâlâ yaygın;
  5.7'de olmayan söz dizimini üretmemek gerekiyor. Hedef sürüm sorulmalı.
- **Yetki kısıtlı.** Paylaşımlı barındırmada kullanıcı çoğu zaman `CREATE
  DATABASE` yapamaz, sadece var olana yazabilir. Üretilen script buna uymalı.
- Bu iş **golden-file testleriyle** korunmalı — çıktı biçimi de bir DDL çıktısı.

## 🔴 Yapılmayacak

- **Panellere otomatik bağlanıp script çalıştırmak.** Plesk/cPanel API'leriyle
  uzaktan veritabanı yönetimi, çok geniş bir yetki ve çok büyük bir sorumluluk
  demek. Namines dosyayı **üretir**, kullanıcı yükler.
- FTP/SSH kimlik bilgisi istemek. Aynı gerekçe.
- Her panel için ayrı entegrasyon yazmak. Ortak payda **standart SQL dosyası**;
  panel farkı talimat metninde kalır, kodda değil.
