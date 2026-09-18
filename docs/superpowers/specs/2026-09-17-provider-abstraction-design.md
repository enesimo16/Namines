# Hız Sınırı Dayanıklılığı ve Sağlayıcı Soyutlaması — Spec

**Durum:** Uygulandı. Aşağıdaki tasarım bölümleri kararların gerekçesini
saklıyor; gerçekleşen hâli için sondaki "Uygulama durumu" bölümüne bakın.

**Sorun:** Şema üretimi büyük isteklerde çalışmıyor ve sebebi kod değil, tek bir
sağlayıcıya (Groq) ücretsiz katman limitleriyle bağlı olmak.

Canlı ölçüm (bu hesap, `openai/gpt-oss-*`): **dakikada 8.000 token**. 54 tablo
≈ 35.000 çıktı tokenı gerektiriyor. Model tarafında sorun yok —
`gpt-oss-120b`'nin tek çağrı tavanı 65.536, yani kapasite fazlasıyla yeterli.
Tıkanan tek şey dakikalık akış.

Bugün 429 alındığında `AiRateLimitException` fırlatılıyor, sağlayıcının verdiği
`Retry-After` değeri de taşınıyor — ama **hiç kullanılmıyor**. Kullanıcı 20
saniye beklemek yerine hatayı görüyor.

## Kullanıcı kararları

1. **Bekleme:** şimdilik uzun beklemeye izin verilsin, ama **Dev Tier'a
   geçildiğinde bu hiç yaşanmamalı.** → Süre sabit değil, **yapılandırılabilir**
   olacak; bugün cömert bir varsayılan, daha yüksek bir katmanda pratikte hiç
   tetiklenmeyen bir emniyet ağı.
2. **DeepSeek:** anahtarsız yazılacak, canlı doğrulama sonraya bırakılacak.
   (Bu oturumun dersi açıkça kabul edildi: birim testler yeşilken canlıda beş
   ayrı hata çıkmıştı. Bu yüzden Faz 2 "bitti" değil "canlıda doğrulanmadı"
   olarak kapanacak.)
3. **Rol:** sağlayıcı **yapılandırmayla seçilebilir**, varsayılan Groq kalır.

---

## Faz 1: 429'da bekle ve yeniden dene

### Nerede

`GroqAIService.PostAsync` — **tek çıkış noktası**. Bu tercih `max_tokens`
kırpmasıyla aynı gerekçeye dayanıyor: bu sınıfta sekizden fazla çağrı yeri var
ve yenileri ekleniyor; her birine yeniden deneme yazmak, birini unutmanın
zamanla kesinleşmesi demek. Gövde zaten burada kuruluyor ve 429 zaten burada
`AiRateLimitException`'a çevriliyor.

### Kural

- Sağlayıcının `Retry-After` başlığı (ya da hata gövdesindeki süre) **olduğu
  gibi** kullanılır — tahmin edilmez.
- Toplam bekleme `Ai:RateLimitRetry:MaxTotalWaitSeconds` ile sınırlı
  (varsayılan **600**). Bu değer aşılınca bugünkü davranışa dönülür:
  `AiRateLimitException` fırlatılır ve kullanıcı ne kadar gerektiğini öğrenir.
- Tek seferlik bekleme de sınırlı (`MaxSingleWaitSeconds`, varsayılan **60**):
  sağlayıcı saçma bir değer döndürürse istek sonsuza kadar asılı kalmaz.
- `CancellationToken` beklemenin İÇİNDE de geçerli — kullanıcı iptal ettiğinde
  60 saniye beklemeye devam etmek, iptali yok saymak olurdu.
- Yeniden deneme yalnızca **429** için. Diğer hatalar bugünkü gibi anında
  yukarı çıkar; 400'ü yeniden denemek yalnızca aynı 400'ü tekrar almaktır.

### Bilinçli olarak YAPILMAYAN

Üretim ekranına "hız sınırı, 20sn bekleniyor" adımı **bu fazda eklenmiyor.**
`PostAsync` HTTP katmanı; oraya ilerleme bildirimi taşımak, bu spec'in
çözmediği bir bağımlılık zinciri açar. Kullanıcı yine de ilerleme görüyor:
parçalar tamamlandıkça SSE akışına düşüyorlar. Beklemenin görünür olması ayrı
bir iş olarak not edildi.

---

## Faz 2: Sağlayıcı soyutlaması + DeepSeek

### Bugünkü durumun dürüst tespiti

Soyutlama **zaten var**: `IAIService` (7 metot), `IAIFactory`, `AIFactory`, ve
ikinci bir implementasyon olarak `OllamaAIService`. Sorun soyutlamanın yokluğu
değil, **atlanması**: altı tüketici somut `GroqAIService`'i alıyor —
`GatewayController`, `AIDbaService`, `AutomationExecutor`,
`GroqSchemaDraftSource`, `MigrationService`, `SmartSeedService`. Sebebi de net:
ihtiyaç duydukları `AnalyzeSchemaDbaAsync`, `GenerateSmartSeedSqlAsync` gibi
metotlar `IAIService`'te yok.

### Seçilen yaklaşım: sınıfı değil, ALTINDAKİ SAĞLAYICIYI değiştirilebilir yap

Altı tüketiciyi bir arayüze çevirmek yerine, `GroqAIService`'in HTTP tarafını
enjekte edilen bir sağlayıcıya devretmesi tercih ediliyor. Böylece **altı
tüketici hiç değişmeden** DeepSeek'i de kullanabilir hâle geliyor; aksi hâlde
her biri için ayrı bir arayüz genişletme ve çevirme işi çıkardı ve DeepSeek
yalnızca şema üretiminde çalışırdı.

```
IChatCompletionProvider        ← sağlayıcıya özgü olan HER ŞEY burada
  ├─ BaseAddress
  ├─ kimlik doğrulama başlığı
  └─ model kimlikleri + model başına çıktı sınırı (IModelCatalog)

  ├── GroqChatCompletionProvider       (bugünkü davranış, birebir)
  └── DeepSeekChatCompletionProvider   (OpenAI uyumlu — aynı gövde şekli)

GroqAIService
  └─ üst seviye işler: şema üretimi, revizyon, DBA, seed, migration…
     Adresi, anahtarı ve modelleri sağlayıcıdan alıyor.
```

Sağlayıcı seçimi: `Ai:Provider` (`groq` | `deepseek`), varsayılan `groq`.
Tanınmayan değer varsayılana düşer ve **loglanır** — bir yazım hatası yüzünden
uygulamanın açılmaması, yapılandırma hatasının bedelini orantısız kılardı.

### Model kataloğu sağlayıcı-farkında olmalı

`NaiCatalog` bugün Groq'a özgü: model kimlikleri **ve** model başına
`MaxCompletionTokens` içeriyor. Bu iki bilgi sağlayıcıya göre değişiyor, yani
katalog sağlayıcıdan gelmeli. `Flash`/`Standard`/`Pro` kademeleri ürünün kendi
kavramı olarak kalıyor — her sağlayıcı bu üç kademeye kendi modelini eşliyor.

### DeepSeek'in bilinen farkları

- OpenAI uyumlu `chat/completions` — gövde şekli aynı, bu yüzden mevcut
  serileştirme yeniden kullanılabilir.
- Farklı temel adres ve model kimlikleri.
- 429 gövdesi Groq'unkiyle **aynı şekilde olmayabilir**.

  **Uygulamada farklı yapıldı:** `Retry-After` çıkarma sağlayıcıya
  taşınMADI, `GroqAIService.ParseRetryAfter`'da ortak kaldı. Gerekçe: bugün
  ikisi de OpenAI uyumlu ve aynı biçimi kullanıyor; olmadığı KANITLANMADAN
  sağlayıcı başına ayrı bir çıkarıcı yazmak, doğrulanmamış bir farkı kodda
  kalıcılaştırmak olurdu. DeepSeek'in gövdesi farklı çıkarsa bu metot
  arayüze taşınmalı — canlı doğrulamada bakılacak ilk yerlerden biri.
- Araç çağırma (tool calling) desteği modelden modele değişir.
  **Bu da yapılmadı:** `SupportsToolCalling` bugünkü hâliyle
  `GroqAIService`'te duruyor. Aynı gerekçe.

---

## Test stratejisi

- **Faz 1:** 429 → bekle → başarı senaryosu; toplam bütçe aşılınca istisna;
  iptal token'ı beklemeyi kesiyor; 429 DIŞINDAKİ hatanın yeniden
  DENENMEDİĞİ. Hepsi sahte bir `HttpMessageHandler` ile, gerçek ağ olmadan.
- **Faz 2:** her iki sağlayıcının doğru temel adres/başlık/model ürettiği;
  `Ai:Provider` seçiminin doğru sağlayıcıyı verdiği; tanınmayan değerin
  varsayılana düşüp loglandığı; `max_tokens` kırpmasının her iki sağlayıcının
  kendi sınırlarını kullandığı.
- **Regresyon:** Groq yolunun davranışı birebir korunmalı — bugünkü tüm testler
  değişmeden geçmeli. Değişmeleri gerekiyorsa bu, davranışın değiştiğinin
  işaretidir ve durup bakılmalı.

## Kapsam dışı (bu spec için)

- Beklemenin üretim ekranında görünür olması (yukarıda gerekçesiyle).
- Groq 429'unda otomatik DeepSeek'e düşme (kullanıcı "seçilebilir" dedi,
  "otomatik yedek" değil).
- `IAIService`'in yedi metotluk yüzeyini genişletmek — bu yaklaşımla gerek
  kalmıyor.
- DeepSeek'in canlı doğrulaması (anahtar yok; kullanıcı kararıyla ertelendi).

---

## Uygulama durumu

**Faz 1 — 429 yeniden deneme: uygulandı, birim testlerle doğrulandı.**
Canlı bir hız sınırına karşı denenmedi; testler sahte bir aktarım katmanı
kullanıyor ve uykuyu dışarıdan alıyor, yani gerçek zaman harcamıyorlar.

**Faz 2 — sağlayıcı soyutlaması: uygulandı. DeepSeek CANLI DOĞRULANMADI.**
Hesapta DeepSeek anahtarı yok. Bu oturumda, tamamı yeşil bir birim test
paketinin arkasında beş ayrı canlı hata saklandı: ölü model kimliği, yerine
yazılan ikinci ölü kimlik, çıktı token kotası işe yetmeyen model, model
sınırını aşan `max_tokens` (400 ile isteğin KOMPLE reddi), ve istenen
`max_tokens` üzerinden sayılan dakikalık kota. DeepSeek yolundaki model
kimlikleri ve 8.192'lik tavan belgelerden alındı — ilk canlı istekten önce
üçü de doğrulanmalı: kimlikler var mı, kota yetiyor mu, tavan gerçekten bu mu.

**Testler:** 1899 başarılı / 2 başarısız (ikisi de Docker bağımlı ve bu
çalışmadan önce de başarısızdı) / 1901 toplam. **Mevcut hiçbir testi
değiştirmek gerekmedi** — Groq davranışının korunduğunun asıl kanıtı bu.
Ayrıca DI zinciri ayrıca sınanıyor (`ChatProviderDependencyInjectionTests`),
çünkü kayıt hatasını ne derleme ne de birim testler yakalar; bu kod tabanında
tam olarak öyle bir boşluk uygulamayı açılmaz yapmıştı.

## Yapılandırma

| Anahtar | Varsayılan | Ne işe yarıyor |
|---|---|---|
| `Ai:Provider` | `groq` | `groq` veya `deepseek`. Tanınmayan değer Groq'a düşer ve loglanır. |
| `Ai:RateLimitRetry:MaxTotalWaitSeconds` | `600` | **Bir istek boyunca** (tüm sağlayıcı çağrıları toplamında) beklenebilecek süre. `0` yeniden denemeyi kapatır. |
| `Ai:RateLimitRetry:MaxSingleWaitSeconds` | `60` | Tek bir beklemenin tavanı; sağlayıcı süre bildirmediğinde kullanılan süre de budur. |
| `Ai:RateLimitRetry:MaxAttempts` | `10` | Süre bütçesi ne kadar geniş olursa olsun bu sayıdan fazla denenmez. |
| `DeepSeek:ApiKey` | — | DeepSeek seçiliyse zorunlu. |
| `DeepSeek:Models:Flash\|Standard\|Pro` | katalog | Sağlayıcı bir modeli kaldırırsa sürüm beklemeden geçmek için. |

**Daha yüksek bir sağlayıcı katmanına geçildiğinde:** bu beklemelerin HİÇ
oluşmaması hedefleniyor. `MaxTotalWaitSeconds` sıfırlanmamalı, ama log'da
"AI provider rate limited; waiting" satırı görülüyorsa bu, katmanın hâlâ dar
olduğunun işareti sayılmalı. Yeniden deneme bir emniyet ağı, çalışma biçimi
değil.

## Bu çalışmanın ÇÖZMEDİĞİ

Mevcut Groq hesabıyla 54 tablo hâlâ tek seferde çıkmıyor; yalnızca hata almak
yerine bekleniyor. 54 tablo ≈ 35.000 çıktı tokenı, hesap 8.000 TPM veriyor —
yani kabaca dört buçuk dakikalık bir kuyruk. Bunu gerçekten açan şey daha geniş
bir kota (DeepSeek ya da Dev Tier), bu faz değil.

Ayrıca: daha geniş bir kota AKIŞ sorununu çözer, modelin 54 tabloyu gerçekten
SAYIP DÖKECEĞİNİ kanıtlamaz. O hâlâ sınanmadı.


---

## Kod incelemesi sonrası düzeltmeler

İlk uygulamadan sonra yapılan incelemede dört bulgu çıktı; dördü de kapatıldı.

**1. Sonsuz yeniden deneme döngüsü (yüksek).** Tek durdurucu süre bütçesiydi ve
sıfır süreli bir bekleme bütçeden hiçbir şey yemiyordu. Sağlayıcı
`Retry-After: 0` derse döngü hiç bitmiyor, istek kullanıcıya dönmüyor ve
sağlayıcı ağ hızında dövülüyordu. Sıfır gerekmiyordu bile: tekrarlayan bir
"0.017s" 600 saniyelik bütçede otuz beş bin istek demekti.

Daha da kötüsü, bu davranışı sabitleyen bir test yazılmıştı
(`A_zero_or_negative_requested_delay_still_costs_nothing_and_is_allowed`), yani
test paketi hatayı yakalamak bir yana onu şartname sayıyordu.

Düzeltme: `AiRetryPolicy` artık deneme sayısını da sınırlıyor
(`MaxAttempts`, varsayılan 10). Süre bütçesi "ne kadar bekleyeceğiz"i, deneme
sayacı "kaç kere deneyeceğiz"i cevaplıyor; biri diğerinin yerine geçemiyor.

**2. Bütçe istek başına değil çağrı başına (orta).** Ayarın adı
`MaxTotalWaitSeconds`, belgesi "bir istek boyunca" diyordu; gerçekte her
`PostAsync` kendi politikasını kuruyordu. 54 tabloluk bir üretim yedi çağrı
yapıyor, yani söz verilen on dakika yetmiş dakikaya kadar çıkabiliyordu.

Düzeltme: istek kapsamlı `AiRetryBudget` servisi. Aynı istekteki paralel
parçalar tek bütçeyi paylaşıyor — bu yüzden `AiRetryPolicy` kilitli hâle
getirildi. İstek bağlamı olmayan arka plan işleri kendi yerel bütçesini kuruyor.

**3. `Groq:Model` ölü ayardı (orta).** `_modelName` alanına yazılıyor ve hiçbir
yerde okunmuyordu; `appsettings.json` bu anahtarı gönderiyordu. Bir modeli
değiştirmek için onu ayarlayan operatör hiçbir şeyin değişmediğini görürdü,
çünkü gerçek override `Nai:Flash|Standard|Pro`. Ölü alan ve anahtar kaldırıldı.

**4. Hata metni yapılandırma jetonunu kullanıyordu (düşük).** Anahtar eksikken
kullanıcı "groq is not configured on this server" görüyordu — hemen yanındaki
Gemini dalı düzgün yazarken. Sağlayıcılara `DisplayName` eklendi.
