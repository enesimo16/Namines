# Namines — Kullanıcıdan (Enes) Beklenen Kararlar/Hesaplar (34-SENDEN-BEKLENENLER.md)

Bu liste kod eksikliği DEĞİL — hepsinin kodu yazılmış ve test edilmiş; eksik olan yalnızca bir hesap açmak, bir sayı onaylamak ya da bir karar vermek. Son güncelleme: Gate G52 (1136 test yeşil).

## 🔴 Kritik — ürünü şu an durduran ikisi

1. **Disk alanı — 3,8 GB kaldı (EN ACİL).** Control DB container'ı oturumlar boyunca defalarca düştü, elle yeniden başlatılması gerekti. Çözüm: WSL2 VHDX'i `Optimize-VHD` ile sıkıştırmak, kullanılmayan Docker imajlarını temizlemek, eski `bin/`/`obj/` klasörlerini silmek.
2. **Stripe hesabı + DÖRT fiyat kimliği.** Ödeme kodu (checkout, webhook, plan ayrımı, iptal, portal, aylık/yıllık geçiş) tamamen yazılmış ve doğrulanmış — ama Stripe'ta bu fiyatların karşılığı yok, yani ürün **tek kuruş tahsil edemiyor**. Gerekenler: Pro aylık $15, Pro yıllık $150, Team aylık $40, Team yıllık $400 — dört ayrı `price_...` kimliği + `Stripe__WebhookSecret`.
3. **Groq'a kart tanımlama (Developer katmanı) — ARTIK 🔴.** Ücretsiz katman dakikada yalnızca ~6.000 token veriyor; bu oturumda üretim 5'ten fazla kez bu duvara çarptı — ürün kendi geliştirme makinesinde bile uçtan uca test edilemiyor. Kart tanımlamak limiti 10 katına çıkarıyor (60.000 token/dk) ve token fiyatını %25 düşürüyor; abonelik/ön ödeme yok.

## 🟡 Sırada bekleyen

4. **GitHub App (Namines Bot).** Bot'un kodu (App kimlik doğrulama, PR yorumu, status check, `.nsl` okuma, kırılma analizi) gate G43'te tamamlandı ve test edildi — ama kimlik bilgisi (`Github__AppId`, `Github__PrivateKey`, `Github__WebhookSecret`) olmadan bot tek satır yazamıyor.
5. **npm hesabı.** MCP sunucusu paketlenmiş, yayına hazır; `npm publish` ve `git tag v0.1.0` atılmadı. Bunsuz kullanıcılar `npx` ile kuramıyor, tüm depoyu klonlamak zorunda kalıyor.
6. **Public alan adı (`api.namines.com` gibi).** Üretilen OpenAPI/TypeScript SDK'nın taban URL'i buna bağlı; ayrıca GitHub webhook'unun ulaşabileceği bir adres olması GitHub App'in de ön koşulu.

## 🟢 Yalnızca onay bekleyen (varsayılan zaten konmuş)

- Ücretsiz havuz büyüklüğü: 500.000 token/gün (~$3,7/ay).
- Çoklu DB ilişki sınırı: Free 3 / Pro 25 / Team 100.
- Rate limit sayıları: Free 60 / Pro 600 / Team 3.000 / Enterprise 10.000 istek/dakika.
- Redis kararı: kullanılacak mı kullanılmayacak mı (tek sunucuda sorun yok, 2+ sunucuda rate limit anlamını kaybediyor).
- İki bilinçli doküman sapması: `X-Namines-Key` header'ı (Authorization Bearer yerine, JWT'yle karışmasın diye) ve argon2id yerine SHA-256 (API anahtarı zaten 256-bit rastgele, argon2 yalnızca gecikme ekler).
- Neon hesabı: branch DB'ler şu an container ile açılıyor (çalışıyor ama yavaş); Neon anında yapar, tamamen isteğe bağlı.

## Kod dışı işler

- `C:\Users\Enes Yel` dizinindeki yanlış git deposu (remote `automated-recruitment-pipeline`, bu depoyla ilgisi yok) düzeltilmedi.
- Ödeme altyapısı araştırması: Stripe Türkiye'de sınırlı, Paddle/LemonSqueezy değerlendirmesi hâlâ açık.
- `namines.com` alan adı + marka taraması yapılmadı.
