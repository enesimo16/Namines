# 32 — Sakınılması Gerekenler (Reddedilmiş Değil, Ertelenmiş)

> [27-LIFECYCLE-PIVOT.md §3](27-LIFECYCLE-PIVOT.md)'te "kesinlikle yapmamalıyız" diye
> geçen maddelerin yeniden çerçevelenmiş hali. Hiçbiri **kalıcı olarak yasak** değil —
> her biri için "şimdi neden hayır" ve "hangi koşulda evet" ayrı ayrı yazılı. Amaç:
> kapıyı kapatmadan, bugün bu yöne kaynak akıtmayı engellemek.

---

## Nasıl okunmalı

Her madde üç soruya cevap veriyor:
1. **Neden şimdi değil** — somut, rekabet/kaynak gerekçesi
2. **Ne zaman "evet" olur** — hangi ölçülebilir koşul gerçekleşirse yeniden değerlendirilir
3. **Bugün ne yapılabilir (ucuz versiyon)** — tam özelliği yapmadan alınabilecek küçük, düşük riskli adım varsa

---

## 1. Jenerik web/mobil/PWA uygulama üretici

**Neden şimdi değil:** Lovable, Bolt.new, v0, Replit Agent — hepsi "tek prompt'tan
UI+backend+DB+auth+deploy" üretiyor, milyar dolarlık değerlemelerle, devasa
dağıtım gücüyle. Tek geliştirici olarak burada özellik yarışına girmek kaybedilir.

**Ne zaman evet olur:** Eğer Change Review + Console (07) müşteri tabanı oturur ve
müşteriler "artık bize tam bir müşteri-yüzü uygulaması da lazım" diye **tekrarlayan**
şekilde isterse — o zaman jenerik değil, **şemaya sıkı bağlı, veri-odaklı** bir
uygulama üretici olarak (Lovable'ın "her şeyi üret" yaklaşımından farklı,
"var olan production şemanı güvenle dışa aç" yaklaşımıyla) değerlendirilir.

**Bugün ne yapılabilir:** [07-CONSOLE-ADMIN-UI.md §8](07-CONSOLE-ADMIN-UI.md)'deki
Console Eject zaten PWA paketleme imkânı taşıyor (Next.js hedefi PWA olarak
yapılandırılabilir) — ayrı bir "app üretici" inşa etmeden, Console'un doğal bir
uzantısı olarak gelir.

---

## 2. Jenerik otomasyon platformu ("Namines Flow" marka hedefi)

**Neden şimdi değil:** n8n 2.0 (70+ AI node, LangChain, kalıcı ajan hafızası),
Zapier (7.000+ entegrasyon), Make — hepsi 2026'da AI-native olgunluğa ulaşmış.
Kendi otomasyon motorunu yazmak, bunları kötü şekilde yeniden icat etmek olur.

**Ne zaman evet olur:** Eğer Change Review kullanıcıları "şema değiştiğinde otomatik
X yapılsın" taleplerini **spesifik olarak veritabanı olaylarına bağlı** (n8n'in
zaten iyi yaptığı genel iş akışları değil) şekilde tekrar tekrar isterse.

**Bugün ne yapılabilir:** Dar, ucuz versiyon — **otomasyon motoru değil, event
kaynağı** ol. Şema değişti/migration uygulandı/CHECK ihlali oldu gibi olaylarda
zengin bir webhook yayınla; n8n/Zapier/Make bunu tüketsin. Bu zaten
[08-GATEWAY-API.md §9](08-GATEWAY-API.md)'da "Realtime abonelik" ve
[24-ROADMAP.md](24-ROADMAP.md)'de A2.09 Webhook olarak planlı — platform iddiası
olmadan, entegrasyon noktası olarak.

---

## 3. AI Dataset Factory ("Namines Data")

**Neden şimdi değil:** Bambaşka bir alıcı kitlesi (ML mühendisi/veri bilimci),
bambaşka bir problem (fine-tuning verisi), Namines'in bugünkü kullanıcısıyla
(backend/DB mühendisi) neredeyse hiç kesişmiyor. Marka odağını sulandırma riski
en yüksek fikir.

**Ne zaman evet olur:** Dürüst cevap — muhtemelen **hiçbir zaman**, mevcut ürün
kimliğiyle. Eğer bir gün Namines'in Data Factory'si ([06-DATA-PLANE.md §7](06-DATA-PLANE.md))
gerçek/sentetik veri üretiminde çok güçlü hale gelirse VE ayrı bir marka/ürün
olarak (Namines'in bir alt-markası, ayrı konumlandırma) düşünülürse — ama bu,
bu doküman setinin kapsamı dışında bir karar.

**Bugün ne yapılabilir:** Hiçbir şey — [06-DATA-PLANE.md §7](06-DATA-PLANE.md)'deki
Data Factory zaten "backend/test verisi üretimi" amaçlı ve bu yeterli. Dataset/JSONL/
fine-tuning yönü tamamen ayrı tutulmalı.

---

## 4. Database Doctor'ın "% performans iyileştirmesi" iddiası

**Neden şimdi değil:** Bu bir özellik reddi değil, bir **doğruluk** sorunu. "~%32
performans iyileştirmesi" gibi bir sayı, gerçek sorgu telemetrisi (`pg_stat_statements`
vb.) olmadan üretilemez — yapısal analiz (mevcut `AIDbaService`) bunu dürüstçe
iddia edemez. Yanlış/abartılı bir performans iddiası, güven inşa etmeye çalışan
bir üründe **tam tersi etki** yapar.

**Ne zaman evet olur:** Canlı DB'ye bağlanan (BYODB veya Namines-managed) ve
gerçek çalışma zamanı istatistiklerine erişimi olan bir kullanıcı için — yani
[06-DATA-PLANE.md](06-DATA-PLANE.md) (Data Plane) devreye girdikten sonra. O zamana
kadar Database Doctor **yapısal** bulgular versin (eksik index, normalizasyon
şüphesi), sayısal performans vaadi vermesin.

**Bugün ne yapılabilir:** [09-AI-LAYER.md §7](09-AI-LAYER.md)'deki mevcut yapısal
DBA advisor'ı olduğu gibi kullan, ama UI'da "tahmini" değil "yapısal bulgu" dili
kullan — "bu kolonda index yok" evet, "%32 hızlanır" hayır.

---

## 5. Kendi PostgreSQL/Kubernetes cluster'ını işletmek

**Neden şimdi değil:** Tek kişilik ekip için altyapı işletmek, ürün geliştirmeyi
durdurur. [03-ARCHITECTURE.md ADR-04](03-ARCHITECTURE.md) zaten bunu "satın al,
yazma" olarak karara bağlamış — Neon/PlanetScale/Azure SQL kullan.

**Ne zaman evet olur:** Gelir, kendi altyapını işletecek bir ekibi (en az 1
platform mühendisi) finanse edecek seviyeye ulaşınca (~$8-15K MRR sonrası,
[22-BUSINESS-MODEL.md](22-BUSINESS-MODEL.md)'deki büyüme projeksiyonuna göre) — ve o
zaman bile yalnızca ölçek ekonomisi gerçekten anlamlıysa.

**Bugün ne yapılabilir:** Değişmiyor, plan zaten doğru (yönetilen sağlayıcı kullan).

---

## Özet tablo

| Fikir | Şimdi | Koşullu gelecek | Ucuz versiyon bugün var mı |
|---|---|---|---|
| Jenerik app üretici | ❌ | Console müşterisi tekrar isterse | ✔ Console Eject → PWA |
| Jenerik otomasyon platformu | ❌ | DB-özel otomasyon talebi tekrarlanırsa | ✔ webhook/event kaynağı |
| AI Dataset Factory | ❌ | Muhtemelen hiçbir zaman (bu ürün kimliğiyle) | ✖ |
| DBA "%X iyileştirme" iddiası | ❌ (doğruluk sorunu) | Data Plane + gerçek telemetri sonrası | ✔ yapısal bulgu dili |
| Kendi altyapı (PG/K8s) | ❌ | ~$8-15K MRR sonrası, ölçek gerekirse | ✖ (plan zaten doğru) |
