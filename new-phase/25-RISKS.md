# 25 — Risk Kaydı

Skor = Olasılık (1-5) × Etki (1-5). 15+ = kırmızı, 8-14 = sarı, <8 = yeşil.

---

## 🔴 Kritik riskler

### R1 — Kapsam patlaması tek geliştiriciyi ezer (5 × 5 = 25)
**Tanım:** 141 özellik, ~1.800 saat. Faz 1'de zaten bu olmuştu: 2 ayda 21 controller, ama sıfır test ve index desteği yok.
**Erken sinyal:** Faz takvimi 3 haftadan fazla kayarsa.
**Azaltma:**
- v2.0'ı Faz 3'te dondur, sat, sonra devam et ([24](24-ROADMAP.md))
- Her fazın sonunda satılabilir çıktı zorunlu
- P2/P3 özellikleri backlog'da kalır, sprint'e girmez
- Haftalık "bu hafta ne kestim" kaydı tut
**Sahip:** Kurucu · **Durum:** Aktif

### R2 — Console tezi yanlış çıkar (3 × 5 = 15)
**Tanım:** Tüm plan "Console retention yaratır" varsayımına dayanıyor. Kullanıcılar Console'u kullanmazsa, ürün yine tek seferlik bir araç kalır.
**Erken sinyal:** M4'te (hafta 34) Console DAU/proje < 2, ekip kullanıcısı davet oranı < %20.
**Azaltma:**
- **Tezi erken test et:** Faz 3'ü beklemeden, Faz 2 sonunda 20 kullanıcıya kaba bir Console prototipi göster
- Yedek strateji hazır: migration güvenliği + legacy DB yönetimi (kurumsal, daha az ama daha büyük müşteri)
- M4'te karar kapısı tanımlı
**Sahip:** Kurucu · **Durum:** İzleniyor

### R3 — Veri kaybı / kiracı sızıntısı olayı (2 × 5 = 10, ama varoluşsal) 🔴
**Tanım:** Kullanıcının üretim veritabanında veri kaybı veya kiracılar arası veri sızıntısı. Bir kez olursa güven onarılmaz.
**Azaltma:**
- Yıkıcı migration öncesi **zorunlu otomatik yedek**
- İki katmanlı izolasyon (uygulama filtresi + DB RLS)
- 120 kiracı izolasyon testi, CI'da bloke edici
- Destructive operasyon için 2 kişi onayı
- `lock_timeout` + `statement_timeout` her migration'da
- Yıllık sızma testi
**Sahip:** Kurucu · **Durum:** Tasarımda ele alındı

### R4 — LLM sağlayıcılarının bu işi kendi ürünlerine katması (4 × 4 = 16)
**Tanım:** Cursor/Claude Code/Copilot "veritabanı kur ve admin panel üret" özelliğini ekler. Zaten kod üretebiliyorlar; eksik olan altyapı.
**Neden tam ölümcül değil:** LLM sana DB provision edemez, RLS uygulayamaz, ekibine panel açamaz, üretimde migration'ı güvenle uygulayamaz, audit tutamaz. Bunlar **işletilen altyapı** gerektirir.
**Azaltma:**
- Kod üretimine değil, **çalışan ve işletilen altyapıya** yatırım yap
- MCP sunucusu yayınla: LLM'ler Namines'i **araç olarak** kullansın (rakip değil, dağıtım kanalı)
- Kurumsal gereksinimlere (audit, RBAC, uyumluluk) yatırım yap — LLM'ler burayı çözmüyor
**Sahip:** Kurucu · **Durum:** Aktif izleme

---

## 🟡 Yüksek riskler

### R5 — AI maliyeti marjı yer (3 × 4 = 12)
**Erken sinyal:** AI maliyeti / gelir > %10.
**Azaltma:** Semantik cache, prompt caching, model kademelendirme, bağlam kırpma, deterministik yollar, plan bazlı kredi, BYOK, günlük maliyet uyarısı ([09 §8](09-AI-LAYER.md)).

### R6 — Sağlayıcı bağımlılığı (Neon) (3 × 4 = 12)
**Tanım:** Neon fiyat değiştirir, kapanır veya satın alınır. Branch DB özelliği tamamen ona bağlı.
**Azaltma:** `IDatabaseProvider` soyutlaması ilk günden; en az 2 sağlayıcı canlı (Neon + Supabase/RDS); göç runbook'u; sözleşmeli fiyat garantisi iste.

### R7 — Codegen doğruluk hatası müşteriyi vurur (3 × 4 = 12)
**Tanım:** Üretilen DDL/migration sessizce yanlış → müşterinin üretiminde sorun. Faz 1'de bu **zaten vardı** (cascade hatası).
**Azaltma:** Golden-file + 275 gerçek DB doğrulaması nightly; round-trip testi; determinizm testi; "6 motorda %100 doğrulanmış" iddiasını pazarlama argümanı yap.

### R8 — Ücretsiz katman istismarı (4 × 3 = 12)
**Tanım:** Bot kayıtları, kripto madencilik için sandbox kullanımı, AI kredisi çiftlikleri.
**Azaltma:** E-posta doğrulama zorunlu; kart olmadan managed DB yok; sandbox egress `deny-all`; kaynak limitleri + 60 dk TTL; IP/cihaz bazlı hız limiti; anormal davranış tespiti; tek kullanımlık e-posta engelleme.

### R9 — Güvenlik açığı (bilinen borç) (3 × 4 = 12)
**Tanım:** Faz 1'de tespit edilen 8 bulgu ([13 §1](13-SECURITY.md)) düzeltilmezse ilk kurumsal incelemede iş biter.
**Azaltma:** Faz 0'da hepsi kapatılır; CI'da SAST/DAST/dependency/sır tarama; `security.txt`; yıllık pentest.

### R10 — Kurucu tükenmişliği (3 × 5 = 15) 🔴
**Tanım:** 16-18 ay tek başına, gelirsiz, sürekli baskı. **En yaygın startup ölüm sebebi bu.**
**Erken sinyal:** 2 hafta üst üste plan dışı kalma, motivasyon düşüşü, "hiçbir şey ilerlemiyor" hissi.
**Azaltma:**
- Sürdürülebilir tempo (haftada ≤ 40 saat), haftada 1 tam gün izin
- Her fazın sonunda yayınla → dopamin ve geri bildirim
- Build-in-public → topluluk desteği ve hesap verebilirlik
- Kapsamı kesmekten çekinme
- İlk 5 ödeyen müşteri en güçlü motivasyon kaynağıdır — oraya hızlı git
**Sahip:** Kurucu · **Durum:** Aktif

### R11 — Türkiye'den ödeme/faturalama sürtünmesi (3 × 3 = 9)
**Tanım:** Stripe Türkiye'de sınırlı; şirket kurma, döviz, vergi, KDV/MOSS karmaşası.
**Azaltma:** Stripe Atlas / Estonya e-Residency değerlendir; Paddle veya LemonSqueezy (Merchant of Record — vergiyi onlar halleder) alternatifi; mali müşavirle erken görüş. **Bu, kod yazmadan önce çözülmesi gereken bir engel.**

---

## 🟢 Orta ve düşük riskler

| # | Risk | O×E | Azaltma |
|---|---|---|---|
| R12 | CRDT karmaşıklığı beklenenden zor | 3×3=9 | Node `y-websocket` sidecar kullan, kendi implementasyonunu yazma |
| R13 | Kubernetes/gVisor operasyonel yükü | 3×3=9 | İlk 6 ay K8s'siz (Railway); sandbox'ı sonraya bırak veya managed DB ile ikame et |
| R14 | dbdiagram/ChartDB benzer özellikler ekler | 3×3=9 | Hendek özellik değil, üç plane senkronu ve veri sahipliği |
| R15 | MIT lisansı ile rakip SaaS kurulması | 2×4=8 | Yeni bileşenlerde BSL 1.1 ([22 §6](22-BUSINESS-MODEL.md)) |
| R16 | SEO sonuçları 6-9 ay sürer | 4×2=8 | Product Hunt/HN ile paralel; topluluk kanalları |
| R17 | Console'un "yeterince esnek değil" itirazı | 3×3=9 | Overlay özelleştirme + eject; BI aracı olmaya çalışma |
| R18 | Enterprise satış döngüsü çok uzun | 4×2=8 | İlk 9 ay self-serve'e odaklan |
| R19 | Oracle/MariaDB bakım yükü | 3×2=6 | "Extended" katmana koy, golden test var ama öncelik düşük |
| R20 | Yjs doküman boyutu şişer | 2×3=6 | Periyodik sıkıştırma + geçmiş budama |
| R21 | KVKK/GDPR uyumsuzluk cezası | 2×4=8 | DPA, veri ikametgâhı, silme akışı, alt işleyici envanteri |
| R22 | Bağımlılık zinciri saldırısı | 2×4=8 | Lock dosyaları, Dependabot, imzalı imajlar, `npm ci` |
| R23 | Marka/alan adı çakışması | 2×3=6 | Marka taraması yap, `namines.com` al |
| R24 | Ev dizininin git repo olması (mevcut) | 4×2=8 | **Hemen düzelt** — `git add -A` tüm ev klasörünü commit'ler |

---

## Risk azaltma takvimi

| Ne zaman | Aksiyon |
|---|---|
| **Bu hafta** | R24 (git düzeltmesi), R11 (ödeme altyapısı araştırması), R23 (alan adı) |
| Faz 0 | R9 (güvenlik borcu), R7 (test altyapısı) |
| Faz 1 | R5 (AI maliyet kontrolleri), R12 (CRDT kararı) |
| Faz 2 | R6 (ikinci provider), R8 (istismar korumaları), R13 (sandbox kararı) |
| Faz 3 | **R2 (Console tezi testi — en kritik)**, R3 (izolasyon testleri) |
| Faz 4 | R15 (lisans), R16 (GTM sonuçları) |
| Sürekli | R1 (kapsam), R10 (tükenmişlik), R4 (rekabet izleme) |

---

## "Planı öldüren" senaryolar ve çıkış yolları

| Senaryo | Belirti | Plan B |
|---|---|---|
| Console kullanılmıyor | M4'te DAU/proje < 2 | Migration güvenliği + legacy DB yönetimine pivot (Bytebase/Atlas alanı, daha az müşteri ama daha büyük sözleşme) |
| Kimse ödemiyor | Hafta 40'ta MRR < $500 | Ücretsiz katmanı sertçe kıs; kurumsal danışmanlık + araç modeline geç |
| Teknik kapsam yetişmiyor | Hafta 30'da Faz 2 bitmemiş | Data Plane'i tamamen at, sadece BYODB destekle (kullanıcının kendi DB'si) — Console ve Gateway yine çalışır, altyapı maliyeti sıfırlanır |
| AI maliyeti kontrolsüz | AI/gelir > %20 | AI'ı tamamen Pro'ya al, ücretsizde deterministik özellikler bırak |
| Tek başına sürdürülemiyor | Tükenmişlik sinyalleri | Kapsamı v1.8'de dondur (sadece tasarım aracı, ama en iyisi), $9/ay'dan sat, sürdürülebilir küçük bir ürün olarak yaşat |

**Son not:** Bu tablonun en alt satırı önemli — "en iyi şema tasarım aracı" olmak da geçerli bir sonuçtur. Faz 0 ve Faz 1 tamamlandığında elinde satılabilir, saygın ve sürdürülebilir bir ürün olur. Faz 2-5 opsiyoneldir ve ancak veriler doğruladığında yapılmalıdır.
