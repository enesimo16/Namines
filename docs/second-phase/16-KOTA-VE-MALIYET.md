# 16 — Kota, Maliyet ve Token Kilitleri

> Deploy öncesi denetim. **Sorulan soru:** "kullanıcı sayısına göre ayda ne
> öderiz ve kimse tokeni sömüremesin".

---

## Bulunan açıklar ve kapatılışları

| # | Açık | Ne oluyordu | Kapatılışı |
|---|------|-------------|-----------|
| 1 | **Kota ölçmüyordu** | Tur başına sabit **2.500** düşülüyor, sağlayıcının `usage` bloğu hiç okunmuyordu | `IAiUsageTracker` — `PostAsync`'te gerçek `total_tokens` yakalanıyor, kota bununla düşülüyor |
| 2 | **Yarış koşulu (TOCTOU)** | `CheckAsync` sayaca dokunmuyor, `ConsumeAsync` iş bitince düşüyordu; aradaki pencerede N eşzamanlı istek aynı bütçeyi harcıyordu | `TryReserveAsync` — kontrol + düşme tek atomik adım; iş bitince `ReconcileAsync` gerçekle mutabakat, hata olursa `RefundAsync` |
| 3 | **Üretim ucunda hız sınırı yok** | Tek kullanıcı 50 istek atıp sağlayıcının TPM duvarını herkes adına doldurabiliyordu | `ai-generation` politikası: kullanıcı başına 2 eşzamanlı + 1 kuyruk |
| 4 | **`max_tokens` plana bağlı değil** | Ücretsiz kullanıcı 32.000 yazabiliyordu | `MaxTokensFor(tier)` — Free 6.000 / Pro 16.000 / Team 32.000 |
| 5 | **Ücretsiz havuzda açlık** | Havuz 100.000, tavan 20.000 → ilk **5 kullanıcı** havuzu bitiriyor, 6.'dan sonrası duvara çarpıyordu | Adil pay: `pay = havuz / hedefKullanıcı`, kimse payının 2 katından fazlasını alamıyor |
| 6 | **Kimliksiz ağır uçlar** | `codeschema/*` ve `compile/shared-hosting` anonimdi; 2 MB regex ve diske SQLite yazımı sınırsız dövülebiliyordu | `[Authorize]` + `sensitive` hız sınırı |
| 7 | **Model çarpanı ölü koddu** | `NaiCatalog.CostOf` yalnızca testlerde çağrılıyordu; token başına ~4 kat pahalı Pro modelini kullanan biri Flash kullananla aynı kotayı ödüyordu | Kota artık `ölçülen token × model çarpanı`; kullanıcı planına indirgenen modelin fiyatından ücretlendiriliyor |
| 8 | **Gösterilen kota ≠ uygulanan kota** | `/api/quota/status` tavanı kendi hesaplıyordu ve adil paylaşımdan habersizdi — ekranda 20.000 yazarken gerçek tavan 10.000 olabiliyordu | Durum ucu artık `AiQuotaService.PerUserCapAsync`'i okuyor; tek kaynak |

## Adil paylaşım — 5 numaranın ayrıntısı

Eski kurulumda ücretsiz katmanın vaadi **"ilk gelene"** idi. Yeni kural:

```
pay        = DailyTokenPool / MinDailyFreeUsers
tavan      = min(planTavanı, max(pay × 2, kullanışlıTaban))
```

- **`× 2`** — tam paya kilitlemek boşta duran payı çöpe atar, tamamına açmak
  birinin hepsini yemesine izin verir. Team havuzundaki kuralla aynı.
- **Kullanışlı taban (8.000)** — havuzu çok kişiye bölmek herkese işe yaramaz
  bir kırıntı vermek olur. 1.000 token'la kullanıcı şema üretemez ve ortasında
  kesilmek hiç başlamamaktan kötüdür. Taban devreye girerse **daha az** kişiye
  **çalışan** bir bütçe verilir; kalanına havuzun dolduğu dürüstçe söylenir.

Güncel ayar: havuz **2.000.000**, hedef **100 kullanıcı** → pay 20.000, plan
tavanı da 20.000. Yani **100 ücretsiz kullanıcı aynı gün tam hakkını
kullanabiliyor** ve hiçbiri diğerini aç bırakamıyor.

## Birim maliyet

Groq ücretli katman, GPT-OSS 120B, %25 indirimli (girdi **$0.1125/M**, çıktı
**$0.45/M**). Kabaca %60 girdi / %40 çıktı dağılımıyla harmanlanmış:

> **≈ $0.2475 / 1M token**

## Bir iş kaç token?

Ölçülen tek gerçek veri: **bir şema üretimi = 7.288 token** (canlı, Groq'un
kendi `usage` bloğundan). Diğerleri o ölçüme ve yapılandırılmış `max_tokens`
değerlerine dayanan **tahmin** — yeni ölçüm altyapısı bunları zamanla
kesinleştirecek.

| Özellik | ~Token |
|---|---|
| Şema üretimi (draft→inspect→repair) | **7.300** ✅ ölçüldü |
| Alternatif üretimi (09) | ~7.300 |
| Bölgesel revizyon (her biri) | ~5.000 |
| DBA analizi | ~6.000 |
| Mock veri | ~6.000 |
| Dokümantasyon / README | ~5.000 |
| Etki açıklama | ~2.000 |
| Görselden şema (vision) | ~8.000 |

**Kapsamlı bir proje oturumu** (üretim + 1 alternatif + 3 revizyon + DBA +
doküman + mock veri): **~48.000 token**.

Bu sayı planları anlamlı kılıyor:

| Plan | Günlük hak | Kapsamlı proje / gün |
|---|---|---|
| Free (10.000 — 500K havuzda) | 10.000 | **0,2** — bir üretim + birkaç düzenleme |
| Pro | 200.000 | **~4** |
| Team (3 koltuk ortak) | 600.000 | **~12** |

Free katman bilinçli olarak "dene" katmanı: bir şema üretip görmeye yetiyor,
bir projeyi bitirmeye yetmiyor.

## Aylık gider

| Kalem | Günlük token | Aylık |
|---|---|---|
| **Tüm ücretsiz kullanıcılar** (başlangıç havuzu) | 500.000 | **~$3.7** |
| — havuz 1M'e çıkarsa | 1.000.000 | ~$7.4 |
| — havuz 2M'e çıkarsa (tavan) | 2.000.000 | ~$15 |
| Bir Pro kullanıcı, **%100** kullanırsa | 200.000 | ~$1.5 |
| Bir Team, **%100** kullanırsa | 600.000 | ~$4.5 |

**Kritik nokta:** ücretsiz taraf **kullanıcı sayısıyla artmıyor** — havuz
tavanlı. 100 kullanıcı da 10.000 kullanıcı da aynı. Gider yalnızca ödeyen
müşteriyle artıyor ve orada gelir de var.

## Stripe sonrası net kâr

Stripe standart: **%2,9 + $0,30** (yurt içi kart). Uluslararası kart **+%1,5**,
para birimi dönüşümü **+%1**, Billing (abonelik) kullandıkça-öde **+%0,7**.

| | Pro $7,50 | Team $20,00 |
|---|---|---|
| Stripe kesintisi (yurt içi + Billing) | −$0,57 | −$1,02 |
| **Net tahsilat** | **$6,93** | **$18,98** |
| API maliyeti (%100 kullanımda) | −$1,49 | −$4,46 |
| **Net kâr (%100 kullanımda)** | **$5,44** | **$14,52** |
| Marj | %73 | %73 |

Uluslararası kart + dönüşümde (+%2,5) net tahsilat $6,75 / $18,48'e iner —
marj yine %70'in üzerinde.

**Gerçekçi kullanım %100 değil.** Kimse her gün hakkının tamamını yakmıyor;
%20 kullanımda Pro'nun net kârı ~$6,60'a çıkıyor.

**Dikkat edilecek tek yer Pro fiyatı:** Stripe'ın **sabit $0,30**'u $7,50'nin
**%4'ü**. Fiyat düştükçe bu oran büyür — $5'lık bir plan olsaydı sabit ücret
tek başına %6 olurdu. Yıllık ödeme (12 ayı tek işlemde tahsil) bu kalemi
12'ye böler.

## Havuz kademesi

**Düşük başlanıyor: 500K.** Havuz üst üste 3 gün %95+ dolarsa
`PoolPressureAsync` bir üst kademeyi **önerir** (500K → 1M → 2M), ama
**kendiliğinden uygulamaz**.

Otomatik uygulamamak bilinçli: havuzu büyütmek para harcamaktır ve aynı
doluluk iki farklı şey olabilir — "ürün tutuyor, büyüt" ya da "biri kötüye
kullanıyor, önce ona bak". Bir sayaç bu ikisini ayırt edemez. Karar insanın;
`MaxDailyTokenPool` de sert tavan.

Operatör `GET /api/quota/pool-pressure` ile (yalnızca Dev hesabı) kaç gün
dolduğunu, önerilen kademeyi ve **her ikisinin aylık dolar karşılığını**
görüyor.

## Model seçimi — küçültmeli mi?

**Hayır, ve sebebi ölçülebilir:**

1. **Ücretsiz kullanıcılar zaten Pro modeli kullanmıyor.**
   `NaiCatalog.MaxFor(Free) = Standard`. En pahalı model yalnızca ödeyen
   müşteride ve orada marj %73.
2. **Asıl sorun modelin kendisi değil, çarpanın bağlı olmamasıydı.**
   `NaiCatalog.CostOf` üretimde hiç çağrılmıyordu — yani Pro modeli (token
   başına ~4 kat pahalı) kullanan biri Flash kullananla aynı kotayı ödüyordu.
   Bu bağlandı: kota artık `ölçülen token × model çarpanı`.

Model küçültmek kaliteyi düşürür ve ürünün asıl vaadi (çalışan şema) tam da
oradan geliyor. Marj sağlıklıyken kaliteden feragat etmek yanlış takas.

## ⚠️ Dikkat

- **Havuzu büyütmek doğrudan gider demek.** 1M token/gün ≈ $7.5/ay. Değiştirmeden
  önce ölçülen gerçek girdi/çıktı dağılımına bakın — artık gerçekten ölçülüyor.
- **Ölçüm alınamazsa tahmine düşülüyor** ve tahmin **kullanıcının değil,
  bütçenin lehine**: ölçemediğimizde rezervasyon olduğu gibi kalıyor.
- **Hız sınırı eşzamanlılık, pencere değil.** Şema üretimi uzun süren bir iş;
  sabit pencere kullanıcıyı bitmiş bir işten sonra bekletirdi.
- Plan **sunucudan** (`SubscriptionStatus`/`PlanCode`) çözülüyor — istemci Pro
  olduğunu iddia edemez. Bu zaten doğruydu, denetimde teyit edildi.

## 🔴 Yapılmayacak

- **Kotayı istemciye sormak.** Tavan da, plan da, sayaç da sunucuda.
- **Ölçülemeyen harcamayı bedava saymak.** `usage` yoksa tahmin düşülür;
  sıfır düşülmez.
- **Havuzu sınırsız yapmak.** Ücretsiz katmanın maliyeti tahmin edilebilir
  kalmalı; sınırsız bedava kullanım, ödeyen müşterinin hizmetini de bozar.
