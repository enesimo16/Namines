# 31 — Yeni İş Kolları (Lifecycle Pivot Sonrası)

> Önceki oturumda değerlendirilen beş fikir, [22-BUSINESS-MODEL.md §9](22-BUSINESS-MODEL.md)'daki
> ticari değerlendirme kriterleriyle (kim kullanır / neden öder / alternatif / moat)
> tek tek puanlanmış hali. Hiçbiri şu an MVP kapsamında değil — [27](27-LIFECYCLE-PIVOT.md)'in
> "Small → Deep → Reliable → Expand" prensibiyle, Change Review çekirdeği oturmadan
> hiçbiri başlatılmaz. Bu doküman **sıralama** ve **neden** kaydı.

---

## Değerlendirme çerçevesi (her fikir için)

| Soru | |
|---|---|
| Kim kullanır? | segment |
| Neden öder? | somut acı |
| Şu an nasıl çözüyor? | mevcut alternatif |
| Namines neden daha iyi? | moat kaynağı |
| Switching cost yaratır mı? | evet/hayır + neden |
| Segment | indie / startup / ajans / küçük ekip / kurumsal |

---

## 1. Self-healing DBA (Continuous Advisor'ın genişletilmiş hali)

| | |
|---|---|
| Kim | Kurumsal ekip, ajans — DBA'sı olmayan herkes |
| Neden öder | Performans sorununu kendi başına teşhis edemiyor, DBA istihdamı pahalı |
| Şu an nasıl | Datadog DBM, pganalyze, OtterTune — hepsi Postgres/MySQL-öncelikli, kurumsal fiyatlı, İngilizce |
| Namines neden daha iyi | 6 motor + Türkçe açıklama + tek tık düzeltme (migration olarak) |
| Switching cost | Orta — geçmiş sağlık verisi Namines'te birikir |
| Segment | Küçük-orta ekip, kurumsal |
| **Durum** | ⚠ [09-AI-LAYER.md §7](09-AI-LAYER.md)'de "Continuous Advisor" olarak zaten planlı. **Gerçek telemetri olmadan (`pg_stat_statements` vb.) iddiaları abartma** — bkz. [32-DEFERRED-NOT-REJECTED.md](32-DEFERRED-NOT-REJECTED.md) |

## 2. KVKK/uyumluluk denetim raporu

| | |
|---|---|
| Kim | Türk kurumsal müşteri, hukuk/uyumluluk departmanı |
| Neden öder | Tekrarlayan, zorunlu, cezası olan bir yükümlülük |
| Şu an nasıl | Elle, danışmanlık firmasıyla, Excel |
| Namines neden daha iyi | `@tag(pii)` zaten NSL'de planlı ([04-NSL-SCHEMA-IR.md §2](04-NSL-SCHEMA-IR.md)) — otomatik PII envanteri + rapor |
| Switching cost | Yüksek — uyumluluk raporu bir kez üretilince aracı değiştirmek risk |
| Segment | Kurumsal, kamu tedarikçisi |
| **Durum** | En rekabetsiz alan (hiçbir Batılı araç KVKK bilmiyor). Ama NSL'in `@tag(pii)` özelliği önce gelmeli — bağımlılık var |

## 3. MSSQL/Oracle → PostgreSQL geçiş kopilotu

| | |
|---|---|
| Kim | Lisans maliyetinden kaçmak isteyen kurumlar |
| Neden öder | SQL Server/Oracle lisansı yıllık çok pahalı; geçiş riski büyük proje |
| Şu an nasıl | Elle, danışmanlık, ya da AWS SCT/Ora2Pg gibi tek yönlü araçlar (görsel yok, AI yok) |
| Namines neden daha iyi | Round-trip testli (G5'te kurduğumuz tam altyapı), davranışsal eşdeğerlik kanıtı |
| Switching cost | Proje-bazlı, tekrar satış zor ama danışmanlık geliri yüksek |
| Segment | Kurumsal, orta-büyük ölçek |
| **Durum** | **Faz 0'ın en doğrudan ticarileşen çıktısı** — G5'teki golden-file + Testcontainers altyapısı bu ürünün ta kendisi. Ek geliştirme çok az, esas iş satış/pazarlama |

## 4. Ajanslara beyaz-etiket DBA hizmeti

| | |
|---|---|
| Kim | MSSQL/MySQL üstünde müşteri projesi yapan yazılım evleri |
| Neden öder | Kendi DBA'ları yok, müşteriye "dahil" hizmet olarak satabilir |
| Şu an nasıl | Hiç sunmuyorlar veya pahalı üçüncü parti danışman |
| Namines neden daha iyi | Dağıtım problemini çözer — ajans kendi müşteri tabanını getirir (B2B2B) |
| Switching cost | Yüksek — ajansın kendi müşterilerine taahhüt ettiği bir hizmet haline gelir |
| Segment | Ajans (asıl segment), dolaylı olarak onların müşterileri |
| **Durum** | En iyi **dağıtım** fikri — tek tek geliştirici ikna etmek yerine ajans kendi tabanını getiriyor. Ürün olgunlaşınca (Fikir 1 hazır olunca) satış kanalı olarak değerlendirilmeli |

## 5. İhale/tedarik teknik doküman üretici

| | |
|---|---|
| Kim | Kamu ihalesine giren yazılım firmaları |
| Neden öder | İhale şartnamesi veri sözlüğü/güvenlik dokümanı zorunlu kılıyor, elle hazırlamak günler sürüyor |
| Şu an nasıl | Elle Word/Excel |
| Namines neden daha iyi | [12-CODEGEN-EJECT.md §6](12-CODEGEN-EJECT.md)'daki Data Dictionary PDF zaten var — sadece format/şablon uyarlaması |
| Switching cost | Düşük (proje-bazlı, tekrar kullanım az) |
| Segment | Türkiye'ye çok özgü, kamu tedarikçisi |
| **Durum** | En düşük efor/en dar niş. Mevcut Data Dictionary özelliğinin bir varyantı — ayrı geliştirme değil, ihale şablonu eklemek kadar basit. Talep doğrulanırsa hızlı eklenir |

---

## Öneri sıralaması (talep doğrulama sonrası, MVP'den SONRA)

```
1. Fikir 3 (Geçiş kopilotu)   — zaten hazır altyapı, en hızlı ticarileşir
2. Fikir 1 (Self-healing DBA) — Continuous Advisor zaten planlı, telemetri eklenir
3. Fikir 4 (Ajans kanalı)     — dağıtım stratejisi, ürün olgunlaşınca devreye
4. Fikir 2 (KVKK raporu)      — NSL'in @tag(pii)'sine bağımlı, ondan sonra
5. Fikir 5 (İhale dokümanı)   — en düşük öncelik, talep görülürse hızlı eklenir
```

**Hiçbiri şimdi başlamıyor.** [27-LIFECYCLE-PIVOT.md](27-LIFECYCLE-PIVOT.md)'in G8-G17
görev listesi (Impact Analysis + Change Review çekirdeği) bitmeden bu beşine
dokunulmaz — kullanıcının "Small → Deep → Reliable → Expand" prensibi burada da
geçerli.
