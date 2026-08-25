# 02 — Redis Kararı

> **Karar: şimdi kurmuyoruz.** Tek sunucuda hiçbir şey kazandırmaz, bir bakım
> yükü daha getirir. İkinci sunucuyu açtığın gün ~1 saatlik iş.
>
> Bu doküman kararı ve gerekçesini kaydediyor ki altı ay sonra "acaba neden
> yapmamıştık" diye tekrar tartışılmasın.

---

## Redis nedir (ayrı bir veritabanı mı?)

**Hayır.** Kalıcı veri tutmuyor. **Sunucular arası ortak hafıza** — RAM'de
yaşayan, çok hızlı bir not defteri.

| | PostgreSQL | Redis |
|---|---|---|
| Ne için | Kalıcı veri (kullanıcı, proje, şema) | Geçici ortak durum (sayaç, kim online) |
| Kaybolursa | Felaket | Sadece sayaçlar sıfırlanır |
| Hız | Milisaniye | Mikrosaniye |
| Benzetme | Arşiv dolabı | Masadaki post-it |

Namines'te Redis'e **hiçbir kalıcı veri yazılmayacak.** Kaybolduğunda tek olan
şey: istek sayaçları sıfırlanır ve "kim online" listesi bir anlığına boşalır.

---

## Ne zaman gerekli

Redis'in tek işi var: **birden fazla API sunucusu birbirinden haberdar olsun.**

Tek sunucun varken her şey zaten aynı hafızada — Redis hiçbir şey çözmez,
sadece araya bir ağ atlaması ve bakılacak bir servis daha koyar.

| Senaryo | Tek sunucu | 2+ sunucu, Redis YOK |
|---|---|---|
| İstek limiti (dakikada 600) | ✅ Doğru | ❌ Kullanıcı 1200 alır — limit sessizce anlamsızlaşır |
| Canlı işbirliği | ✅ Çalışır | ❌ A sunucusundaki kullanıcı B'dekini görmez |
| "Kim online" | ✅ Doğru | ❌ Herkes birbirini eksik görür |

> ⚠️ **En sinsi olanı ilki.** İki sunucuya çıktığın gün limitler sessizce ikiye
> katlanır. Hata vermez, log basmaz — fark etmenin tek yolu faturaya bakmaktır.
> Bu yüzden ölçeklemeden **önce** Redis, sonra ikinci sunucu.

---

## Kodda durum: 3'ün 2'si zaten hazır

| Parça | Durum | Redis gelince ne olur |
|---|---|---|
| **SignalR backplane** | ✅ Yazıldı (G6) | `.env`'e tek satır — kod değişmez |
| **Presence store** (kim online) | ✅ `RedisPresenceStore` var | `.env`'e tek satır — kod değişmez |
| **Gateway rate limiter** | ❌ Bellek içi | Bu sınıfın gövdesi yazılacak (~1 saat) |

Yani bugün `.env`'e şu satırı koysan **ikisi anında açılır**:

```
Redis__ConnectionString=localhost:6379
```

Kod tarafında bunlar için yapılacak bir şey yok. Yapılandırma yoksa sistem
sessizce tek-instance moduna düşüyor ve **açılışta uyarı basıyor** — sürprize
yer bırakmamak için.

### Kalan tek iş: rate limiter

[`GatewayRateLimiter`](../backend/Namines.Infrastructure/Data/GatewayRateLimiter.cs)
şu an `ConcurrentDictionary` ile sabit pencere (fixed window) sayıyor.

Sınıfın kendi yorumunda zaten yazıyor: *"Redis geldiğinde yalnızca bu sınıfın
gövdesi değişir."* Arayüzü (`TryAcquire`) aynı kalacak, çağıran hiçbir yer
değişmeyecek.

**Sürgülü pencere (sliding window) neden seçilmemişti:** istek başına zaman
damgası listesi tutmayı gerektiriyor, yani bellekte istemci başına sınırsız
büyüyebilen bir yapı. Sınır koymak için bellek sızdırmak yanlış takas. Redis'te
bu kısıt kalkıyor (TTL var), o yüzden Redis sürümünde sürgülü pencereye
geçilebilir.

---

## Ne zaman geri döneceğiz

Şu üçünden **biri** olduğunda:

1. **İkinci API sunucusu açılıyor** — sebep ne olursa olsun (yük, yedeklilik)
2. **Gateway ciddi trafik alıyor** — limitlerin gerçekten doğru olması para
   meselesi hâline geliyor
3. **Canlı işbirliği çok sunucuya yayılıyor** — aynı şemada 2 kişi ama farklı
   sunuculara düşüyorlar

Bunlardan hiçbiri olmadan Redis eklemek, **kullanılmayan bir bağımlılığı
üretimde ayakta tutmak** demek: bir servis daha, bir bağlantı hatası kaynağı
daha, bir bakım kalemi daha.

---

## Kurmaya karar verirsen (brief)

**Ne yapman lazım:**
1. Bir Redis örneği (yerelde `docker run -p 6379:6379 redis:7-alpine`;
   üretimde Upstash / Redis Cloud ücretsiz katmanı yeterli)
2. `.env`'e: `Redis__ConnectionString=...`

**Ben ne yaparım:**
1. Rate limiter'ı Redis token bucket'a çeviririm (arayüz aynı kalır)
2. İki instance'ı aynı anda ayağa kaldırıp **limitin gerçekten paylaşıldığını**
   kanıtlarım — çünkü bu tam da "çalışıyor görünüp çalışmayan" cinsten bir iş
3. Redis düştüğünde ne olacağını yazarım: **isteği reddetme, belleğe düş.**
   Sayaç servisinin arızası kullanıcının isteğini düşürmemeli — o zaman Redis,
   çözdüğünden fazla sorun çıkarır

**Süre:** ~1 saat kod + doğrulama.

---

**İlgili:** [../new-phase/08-GATEWAY-API.md](../new-phase/08-GATEWAY-API.md) §5-6,
[../new-phase/34-SENDEN-BEKLENENLER.md](../new-phase/34-SENDEN-BEKLENENLER.md) §5
