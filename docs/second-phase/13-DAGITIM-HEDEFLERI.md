# 13 — Dağıtım Hedefleri (Plesk, cPanel, mobil/SQLite)

> **Sıra: 10.** İlk değerlendirmemde "niş" demiştim — **haksızdım.** Aşağıda
> neden fikrimi değiştirdiğim yazıyor.
>
> ✅ **Yapıldı.** `SharedHostingExporter` (yeni motor değil, var olan DDL
> üreticilerinin çıktısını biçimlendiren bir katman) + `POST
> /api/compile/shared-hosting` (bedava, AI yok) + canvas'ta "Shared Hosting /
> Mobile" dışa aktarma seçeneği. MySQL/MariaDB hedefi: utf8mb4 açıkça
> zorlanıyor (MySQL üreticisi bunu yazmadığı için yalnızca bu ihracat
> yolunda düzeltildi — temel üretici ve golden-file testleri değişmedi),
> `SET FOREIGN_KEY_CHECKS=0/1` sarmalanıyor, `CREATE DATABASE` hiç
> üretilmiyor, dosya boyutu sınırını aşan şemalar ifade sınırında bölünüyor
> (`SqlFileSplitter`), adım adım Plesk/cPanel/phpMyAdmin talimatı README
> olarak ekleniyor. SQLite/mobil hedefi: DDL gerçek bir geçici dosyaya karşı
> çalıştırılıp **gerçekten açılabilir bir .db dosyası** üretiliyor
> (`SqliteFileBuilder`, `Microsoft.Data.Sqlite` — zaten proje bağımlılığıydı),
> iOS/Android/Flutter için kısa gömme örnekleriyle. Zip içeriği canlı `curl`
> ile doğrulandı: gerçek SQLite dosya imzası (`SQLite format 3`), gerçek
> `utf8mb4`/`FOREIGN_KEY_CHECKS` içeren MySQL SQL'i.
>
> **Ele alınmayan tek nokta:** MySQL 5.7 sözdizim uyumluluğu gerçek anlamda
> (motor sürümüne göre farklı DDL üretmek) yapılmadı — kod tabanında
> `DatabaseType` hiç sürüm ayrımı taşımıyor, bunu eklemek her üreticiye
> yayılan büyük bir değişiklik olurdu. Bunun yerine dürüst bir orta yol:
> şemada CHECK kısıtı varsa üretilen dosyaya "MySQL 5.7 bunu kabul eder ama
> uygulamaz" uyarısı ekleniyor — sessizce yanlış değil, ama otomatik
> uyumluluk da değil.

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

1. ✅ **phpMyAdmin uyumlu SQL** — tek dosya, `SET FOREIGN_KEY_CHECKS`, doğru
   sıralama (üretici zaten bağımlılık sırasına göre yazıyordu)
2. ✅ **Yükleme boyutu bölme** — `SqlFileSplitter`, yalnızca ifade sınırında
3. ✅ **Adım adım talimat** — README.txt, panel-agnostik (Plesk/cPanel/DirectAdmin ortak akış)
4. ✅ **SQLite mobil paketi** — hazır `.db` (gerçekten çalıştırılıp doğrulandı) + iOS/Android/Flutter gömme örneği

## ⚠️ Dikkat

- **Karakter seti tuzağı.** ✅ Ele alındı — `utf8mb4` MySQL ihracat yolunda açıkça yazılıyor (MariaDB üreticisi zaten yazıyordu).
- **MySQL sürümü eski olabilir.** 🔶 Kısmen — gerçek sürüm-bazlı DDL üretimi
  yapılmadı (bkz. yukarıdaki not), yalnızca CHECK kısıtı varsa uyarı ekleniyor.
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
