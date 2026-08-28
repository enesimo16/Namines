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

## Aylık maliyet

Groq ücretli katman, GPT-OSS 120B, %25 indirimli (girdi **$0.1125/M**, çıktı
**$0.45/M**), kabaca %60 girdi / %40 çıktı:

| Kalem | Günlük token | Aylık |
|---|---|---|
| **Tüm ücretsiz kullanıcılar** (havuz tavanı) | 2.000.000 | **~$15** |
| Bir Pro kullanıcı (200K/gün, tamamı kullanılırsa) | 200.000 | ~$1.5 |
| Bir Team (600K/gün) | 600.000 | ~$4.5 |

**Kritik nokta:** ücretsiz taraf **kullanıcı sayısıyla artmıyor** — havuz
tavanlı. 100 kullanıcı da 10.000 kullanıcı da aynı ~$15. Maliyet yalnızca
ödeyen müşteri sayısıyla artıyor ve orada gelir de var.

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
