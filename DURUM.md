# Namines — Durum ve kalan işler

> Bu belge **ne bittiğini ve neyin KANITLANDIĞINI** ayırır. "Yazıldı" ile
> "gerçek bir sisteme karşı çalıştığı görüldü" aynı şey değil; karışması, en
> çok ihtiyaç duyulan anda çalışmayan bir özellik demek.

---

## Kanıt seviyeleri

| Seviye | Anlamı |
|---|---|
| ✅ **Canlı kanıtlandı** | Gerçek bir sisteme karşı çalıştırıldı ve sonucu bağımsız bir araçla (psql, mc, docker) doğrulandı |
| 🟡 **Test edildi** | Otomatik testi var ama gerçek dış sisteme karşı hiç çalışmadı |
| ⚪ **Yazıldı** | Derleniyor, kanıtı yok |
| ❌ **Yok** | Bilinçli olarak kapsam dışı |

---

## Namines Vault (yedekleme)

| Yetenek | Durum | Kanıt |
|---|---|---|
| PostgreSQL yedek/geri yükleme | ✅ | Yedek → veriyi boz → geri yükle → bağımsız `psql` ile satır kontrolü |
| AES-256-GCM şifreleme | ✅ | Tek bayt çevrildi → geri yükleme reddedildi, veri el değmedi |
| Bütünlük doğrulama (V5) | ✅ | Şifresi ÇÖZÜLEN ama geçerli arşiv OLMAYAN dosya üretildi → reddedildi |
| Zamanlanmış yedek (V4) | ✅ | İki API instance aynı anda uyandı → **tek** yedek alındı |
| Saklama politikası | ✅ | `retain=1` ile eski otomatik yedek silindi, elle alınanlara dokunulmadı |
| Nesne depo (S3/MinIO) | ✅ | Gerçek MinIO: yedek nesne olarak yazıldı, ondan geri yüklendi, doğrulandı |
| MySQL/MSSQL/Oracle yedek | ❌ | v1 yalnızca PostgreSQL — yazılıp doğrulanmayan motor "destekleniyor" sayılmaz |
| Kısmi (tek tablo) geri yükleme | ❌ | v2 |

**Deploy şartı:** API'nin Docker daemon'una erişimi gerekiyor; bugünkü
`docker-compose.yml` bunu bilinçli olarak vermiyor. Ayrıntı ve seçenekler:
[`namines-vault/02-ERISIM-VE-DEPLOY.md`](namines-vault/02-ERISIM-VE-DEPLOY.md).
`GET /api/vault/health` bu durumu açıkça söylüyor.

---

## Namines Ground (yönetilen veritabanı)

| Yetenek | Durum | Kanıt |
|---|---|---|
| Provizyon (LocalPostgres) | ✅ | Veritabanı + rol oluştu; `datacl`'de PUBLIC **yok**, rol `SUPERUSER` değil |
| İdempotans | ✅ | İkinci provizyon aynı kaydı döndü, sunucuda hâlâ tek veritabanı |
| Silme + geri alma | ✅ | PendingDelete → bağlantı kalktı, veritabanı durdu → geri alma → yeniden bağlanıldı |
| Kalıcı silme (bekleme penceresi) | ✅ | Pencere 0'a çekildi → veritabanı ve rol sunucudan gitti |
| Kullanım ölçümleri | ✅ | Gerçek sayılar (7.7 MB, 1 bağlantı) PostgreSQL'den okundu |
| Plan kotası | 🟡 | Kod ve testi var; kota aşımı senaryosu canlı denenmedi |
| **Neon sağlayıcısı** | ⚪ | **API anahtarı yok.** 7 birim testi sahte istemciyle geçiyor; canlı Neon'a karşı HİÇ çalıştırılmadı ve arayüz bunu "⚠ Canlı denenmedi" diye yazıyor |
| Copy-on-write branch | ❌ | v2 |
| Çok bölgeli | ❌ | v2 |

---

## Namines Desk

| Yetenek | Durum |
|---|---|
| Veri görüntüleme/düzenleme, SQL konsolu, denetim kaydı, ekip | ✅ |
| Yedekler görünümü | ✅ |
| Barındırma görünümü | ✅ |

Ground ve Vault'un **kendi siteleri yok** — ikisi de arka uç modülü ve
kullanıcıya bakan tek yüzleri Desk içindeki görünümler. Gerekçe:
[`namines-vault/02-ERISIM-VE-DEPLOY.md`](namines-vault/02-ERISIM-VE-DEPLOY.md) §1.

---

## Şema okuma (introspection)

| Motor | Durum | Not |
|---|---|---|
| PostgreSQL | ✅ | Canlı kullanımda |
| MySQL | ✅ | Gerçek MySQL 8'e karşı test edildi (`MySqlIntrospectionTests`) |
| MSSQL | ⚪ | **Bu makinede çalıştırılamıyor** (Docker VM 1.9 GB, motor 2000 MB istiyor) |
| Oracle | ⚪ | İmaj yok, diskte yer yok |

> MySQL testi yazılınca **iki gerçek hata** çıktı ve düzeltildi:
> yabancı anahtar ilişkileri hiç okunmuyordu (Canvas çizgi çizmiyor, DDL
> yeniden üretilirken FK'lar sessizce kayboluyordu) ve `AUTO_INCREMENT`
> görülmüyordu. Aynı ilişki boşluğu MSSQL ve Oracle'da da vardı; sorgular
> yazıldı ama o iki motor canlı doğrulanamadı.

---

## Testler

| Paket | Sonuç |
|---|---|
| `Namines.Tests` (birim) | 1320+ geçiyor |
| `Namines.Tests` (entegrasyon) | Gerçek konteynerlere karşı geçiyor; MSSQL'e bağlı olanlar **gerekçesiyle atlanıyor** |
| `Namines.Tests.RunTests` | 15 geçiyor, 2 atlanıyor (MSSQL) |
| Desk (vitest) | 99 geçiyor |

**Atlama ≠ geçme.** Bir testin çevresel bir kısıt yüzünden kırmızı yanması
"testler zaten kırmızı" alışkanlığı yaratır ve gerçek hataları görünmez kılar;
bu yüzden motor barındırılamıyorsa test **gerekçesini yazarak** atlanıyor.
CI'da Docker'a yeterli bellek verilmeli — orada atlanan test bir uyarıdır.

> ⚠️ **Koşu sonucunu okurken atlanan sayısına bakın.** Bir kez şu yaşandı:
> derlemenin hemen ardından koşan `docker info` yüklü makinede zaman aşımına
> uğradı, prob "Docker yok" dedi ve **127 integration testi sessizce atlandı** —
> suite yine "Başarılı!" yazdı. Yeşil ama hiçbir şey kanıtlamayan bir koşu,
> kırmızı bir koşudan daha tehlikeli. Prob artık 30 saniye bekliyor ve bir kez
> yeniden deniyor, ama sayıya bakma alışkanlığı yerini tutmaz.

---

## Senden bekleyenler

| # | Konu | Neden gerekli |
|---|---|---|
| 1 | **`NEON_API_KEY`** | Neon sağlayıcısı canlı doğrulanamıyor. Anahtar gelince provizyon → bağımsız `psql` → silme turu koşulacak |
| 2 | Docker Desktop belleği (≥ 3 GB) | MSSQL'e bağlı testler ve "Run Tests"in MSSQL yolu bu makinede hiç çalışamıyor |
| 3 | Stripe fiyat kimlikleri | Ödeme akışı yapılandırılmamış |
| 4 | Üretimde nesne depo bilgileri | `Vault__S3__*` — yoksa yedekler sunucu diskinde kalır |
