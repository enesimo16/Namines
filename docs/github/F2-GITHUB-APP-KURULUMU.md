# F2 — GitHub App Kurulumu (senin yapacağın kısım)

> Bu doküman **koddan çıkarıldı**, tahminden değil: aşağıdaki her ayar adı ve
> her olay adı, uygulamanın gerçekten okuduğu/dinlediği değerdir
> (`GithubClient.cs`, `GithubWebhookController.cs`, `GithubBotService.cs`).

## Neden gerekiyor

Bot'un mantığı hazır ve test edilmiş; eksik olan tek şey GitHub'a **"ben
Namines'im"** diyebileceği kimlik. Kimlik bilgisi yoksa bot yazmayı denemiyor
ve olayı `"Accepted. Posting back to GitHub needs the Namines GitHub App
credentials."` diye cevaplıyor — sahte bir başarı raporlamıyor.

App ayrıca **kota sorununu da çözüyor**: anonim okuma saatte 60 istek (ve bu
sınır IP başına, yani sunucunun tamamı için); App kimliğiyle 5000/saat.

---

## 1. App'i oluştur

GitHub → sağ üst avatar → **Settings** → sol altta **Developer settings** →
**GitHub Apps** → **New GitHub App**.

> Şirket/organizasyon adına kuracaksan: organizasyon sayfası → **Settings** →
> **Developer settings** → **GitHub Apps** → **New GitHub App**. Kişisel
> hesapta oluşturulan App'i sonradan organizasyona taşımak zahmetli, baştan
> doğru yerde aç.

| Alan | Ne yazacaksın |
|---|---|
| **GitHub App name** | `Namines` (alınmışsa `Namines Bot` gibi bir varyant — ad global olarak benzersiz) |
| **Homepage URL** | Ürünün adresi; yoksa depo adresi |
| **Webhook → Active** | ✅ işaretli kalsın |
| **Webhook URL** | API'nin **public** adresi + `/api/github/webhook` |
| **Webhook secret** | Kendi ürettiğin rastgele uzun bir dize — birazdan `Github__WebhookSecret` olacak |
| **Where can this app be installed?** | Yalnızca sen kullanacaksan "Only on this account"; müşteriler kuracaksa "Any account" |

### Webhook URL'i için: makinen dışarı açık değilse

Geliştirme sırasında GitHub senin `localhost`'una ulaşamaz. İki yol:

- **GitHub'ın kendi aracı:** App ayarlarında **Advanced → Deliveries**
  ekranından gelen olayları görüp **Redeliver** edebilirsin; ayrıca
  `smee.io` benzeri bir yönlendirici kullanabilirsin.
- **Bir tünel:** `cloudflared tunnel --url http://localhost:5000` ya da
  `ngrok http 5000`. Verdiği public adres + `/api/github/webhook` yazılır.
  Tünel adresi her yeniden başlatmada değişirse App ayarından güncellemen
  gerekir.

## 2. İzinler — **bu kademede sadece üç tane**

**Permissions & events → Repository permissions:**

| İzin | Seviye | Neden |
|---|---|---|
| **Contents** | Read-only | PR'daki şema dosyasını ve depo ağacını okumak |
| **Pull requests** | Read & write | PR'a risk yorumu yazmak |
| **Checks** | Read & write | `namines/schema-review` status check'i oluşturmak |

> **`Contents: Read & write` ŞİMDİ İSTENMİYOR.** Depoya yazma (PR açma, push)
> F5'te, senin ayrı bir izin onayınla geliyor. Baştan geniş izin istemek,
> kurulum ekranında kullanıcının durup vazgeçtiği yerdir
> ([04-YAZMA-YOLU-VE-IZIN.md](04-YAZMA-YOLU-VE-IZIN.md)).

## 3. Olaylar — **ikisi yeterli**

**Subscribe to events** altında:

- ✅ **Pull request** — bot `opened`, `reopened`, `synchronize` eylemlerinde
  çalışıyor; diğerlerini "şemayı değiştirmiyor" diye yok sayıyor.
- ✅ **Issue comment** — `/namines ...` komutları için (`created` eylemi).

Başka olay işaretlemene gerek yok; bot yalnızca bu ikisine cevap veriyor,
gerisini `"Ignored: <event> is not an event the bot acts on."` diye geçiyor.

## 4. Üç değeri al

App oluştuktan sonra App'in **General** sekmesinde:

1. **App ID** — sayfanın üstünde yazan sayı.
2. **Private key** — aşağıda **Generate a private key** → bir `.pem` dosyası
   iner. **Bu dosya bir daha indirilemez**, sakla.
3. **Webhook secret** — 1. adımda kendi yazdığın dize.

## 5. Değerleri uygulamaya ver

Uygulama şu anahtarları okuyor (ikisi de çalışır — yapılandırma anahtarı ya da
ortam değişkeni):

| Yapılandırma | Ortam değişkeni | Değer |
|---|---|---|
| `Github:AppId` | `GITHUB_APP_ID` | App ID |
| `Github:PrivateKey` | `GITHUB_APP_PRIVATE_KEY` | `.pem` dosyasının **içeriği** (yolu değil) |
| `Github:WebhookSecret` | `GITHUB_WEBHOOK_SECRET` | Webhook secret |
| `Github:SchemaPath` | — | Depodaki şema dosyasının yolu. Varsayılan: `schema.nsl` |

`.pem` içeriği çok satırlı; ortam değişkenine verirken satır sonlarının
korunduğundan emin ol. Docker Compose'da blok skaları (`|`) ile, Windows'ta
tek tırnak içinde çok satırlı değer vererek.

> **Bu üç değer birer sırdır.** Depoya commit'leme; `.gitignore`'da zaten
> `.env` türü dosyalar var ama `.pem`'i projeye koyma.

## 6. App'i bir depoya kur

App'in **Install App** sekmesi → hesabını seç → **Only select repositories** →
denemek istediğin depo.

Kurulum tamamlandığında GitHub, webhook'a bir `installation` olayı gönderir ve
bot artık o depodaki PR'ları görür.

## 7. Çalıştığını nasıl anlarsın

1. Depoda `schema.nsl` (ya da `Github:SchemaPath` ile verdiğin yol) bulunan bir
   dal aç, içinde bir tabloyu **sil** ve PR aç.
2. PR'da iki şey görünmeli:
   - Namines'in **yorumu**: risk tablosu ve etkilenen tablolar.
   - **`namines/schema-review`** status check'i — yıkıcı değişiklik olduğu için
     `failure`.
3. GitHub → App → **Advanced → Recent Deliveries**: isteğin gövdesini ve
   sunucunun cevabını görebilirsin. Cevap
   `"Accepted. Posting back to GitHub needs the Namines GitHub App credentials."`
   ise değerler uygulamaya ulaşmamış demektir.
4. Depo ayarları → **Branches** → branch protection → **Require status checks**
   → `namines/schema-review` seç. Artık yıkıcı şema değişikliği merge
   edilemiyor. **Ürünün en kısa değer cümlesi bu.**

## Sık karşılaşılan üç hata

| Belirti | Sebep |
|---|---|
| Webhook 401/403 dönüyor, log'da "signature" | `Github:WebhookSecret` ile App'teki secret aynı değil. Sunucu sebebi bilerek söylemiyor (saldırgana kurulum bilgisi vermemek için), ayrıntı log'da |
| Bot cevap veriyor ama PR'a hiçbir şey yazmıyor | `AppId`/`PrivateKey` ulaşmamış — bot kimlik yoksa yazmayı DENEMİYOR |
| Log'da `resource not accessible by integration` | İzinler eksik; §2'deki üç izni kontrol et. İzin değiştirdiğinde **kurulumu yeniden onaylaman** gerekir (GitHub e-posta/afiş ile ister) |

---

## Sen bunu yaparken bende ne var

App gelene kadar bloke olmayan işler:
[07-FAZ-PLANI.md](07-FAZ-PLANI.md)'deki F3'ün eşleme modeli ve F5'in izin
modeli tasarımı yazılabilir; ama ikisinin de **canlı doğrulaması** App'e bağlı,
o yüzden asıl sıra App geldikten sonra başlıyor.
