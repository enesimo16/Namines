# 07 — Analytics

> Referans: Vercel **Analytics / Observability / Usage**. Kart ızgarası, zaman
> serisi grafikleri, dönem seçici düzeni alınıyor.
>
> **Metrikler alınmıyor** — sebebi aşağıda.

---

## 1. Neyi gösteremeyiz (ve neden)

Vercel'in ekranındaki kutular:

| Vercel metriği | Namines'te durumu |
|---|---|
| Edge Requests | Namines kimsenin uygulamasını çalıştırmıyor. **Karşılığı yok.** |
| Fluid Active CPU | Aynı. **Karşılığı yok.** |
| Fast Origin Transfer | Aynı. **Karşılığı yok.** |
| ISR Reads | Next.js'e özgü. **Karşılığı yok.** |
| Function Error / Timeout | Aynı. **Karşılığı yok.** |
| Sayfa görüntüleme / ziyaretçi | `@vercel/analytics` istemci betiği gerektirir; Desk kullanıcının sitesine kod koymuyor. **Karşılığı yok.** |

Bu kutuları benzer isimlerle doldurmak **uydurma sayı** göstermek olurdu.
`third-phase` ve `CHECKLIST.md` boyunca korunan kural: kaynağı olmayan sayı
gösterilmez.

### `NaminesMetrics` neden kullanılamıyor

Projede OpenTelemetry sayaçları var (`namines_gateway_requests_total`,
`namines_schema_compilations_total` …). Ama:

- **Proje bazlı değil** — etiketleri `operation`/`outcome`, `projectId` yok
- **Veritabanında değil** — bir metrik toplayıcıya (Prometheus vb.) akıyor;
  Desk'in sorgulayabileceği bir depo yok

### `UsageEvent` neden yetmiyor

Fatura için gerçek olay kayıtları tutuluyor (`AiCall`, `ApiRequest`, …) ama
**`UserId` bazlı, `ProjectId` yok.** Kullanıcının 3 projesi varsa hangisinin ne
kadar tükettiği ayrılamıyor.

> Proje bazlı kullanım gerçekten istenirse `UsageEvent`'e `ProjectId?` eklemek
> gerekir — küçük bir migration ama **faturalama yolunu** etkiliyor, bu yüzden
> Desk'in kendi başına alacağı bir karar değil.

---

## 2. Neyi gerçekten gösterebiliriz

Tek gerçek proje-bazlı zaman serisi kaynağı: **`GatewayAuditEntry`**.
`ProjectId`, `CreatedAt`, `Kind`, `TableName`, `Succeeded`, `AffectedRows`
alanları var — bu, anlamlı bir analitik ekranı için yeterli.

### Kartlar

| Kart | Hesap | Değeri |
|---|---|---|
| **Yazma işlemi** (zaman serisi) | `COUNT(*)` gün/saat bazında, `Kind`'a göre yığılmış | Verinin ne zaman değiştiğini gösterir |
| **Başarı oranı** | `SUM(Succeeded)/COUNT(*)` | Bozuk bir entegrasyonu erken yakalar |
| **Etkilenen satır** | `SUM(AffectedRows)` | Tek bir `Import`'un hacmini görünür kılar |
| **En çok yazılan tablolar** | `GROUP BY TableName` ilk 10 | Sistemin sıcak noktası |
| **Kaynak dağılımı** | insan (`ActorUserId`) vs. uygulama (`ApiKeyPrefix`) | Trafiğin nereden geldiği |
| **Şema değişikliği** | `SchemaVersion` sayısı / dönem | Yapının ne sıklıkta değiştiği |
| **Tablo / kolon / ilişki sayısı** | Canlı şemadan | Şemanın büyüme eğrisi |

Hepsi tek tablodan ve `SchemaVersion`'dan türetiliyor — **yeni veri toplama
gerekmiyor.**

### Dönem seçici

Vercel'deki gibi: Son 24 saat · 7 gün · 30 gün. `CreatedAt` üzerinde aralık.

---

## 3. Gereken backend

Yeni uç: `GET /api/gateway/analytics/{projectId}?from=&to=&bucket=hour|day`

Dönen: zaman kovaları + tür kırılımı + toplamlar + ilk-N tablo.

**Neden istemcide hesaplanmıyor:** 30 günlük denetim kaydı on binlerce satır
olabilir; hepsini tarayıcıya indirip orada gruplamak hem yavaş hem gereksiz
veri taşıması. Toplama SQL'de yapılır.

**Yetki:** Logs ile aynı (Admin ve üstü) — toplamlar da veri hareketini
açığa vuruyor.

---

## 4. ⚠️ Boş durum, "sıfır" ile karıştırılmamalı

Denetim kaydı yalnızca yazma tuttuğu için, **sadece okuma yapan** bir proje
bu ekranda tamamen boş görünür. Bu "kullanım yok" demek DEĞİL.

Boş durum metni bunu açıkça söylemeli:
*"Bu dönemde yazma işlemi yok. Okuma istekleri kaydedilmiyor."*

Aksi hâlde kullanıcı projesinin ölü olduğunu sanır.

---

## 5. Grafik tercihleri

- Kütüphane: hafif bir grafik paketi (Desk'in **kendi** bağımlılığı; ana
  uygulamadan bileşen kopyalanmayacak)
- Zaman serisi: alan grafiği, tür bazında yığılmış
- Renk: semantik ayrım yalnızca **başarı/hata**; türler nötr tonlarda
  (tasarım sistemi [`08`](08-TASARIM-SISTEMI.md))
- Eksende `tabular-nums`, y ekseni her zaman 0'dan başlar

> Y eksenini 0'dan başlatmamak küçük dalgalanmayı felaket gibi gösterir —
> analitik ekranında bu, yanlış karar aldıran bir görsel yalan.

---

## 6. Kabul kriterleri

| # | Kriter | Kanıt |
|---|---|---|
| 1 | Desk'ten yapılan CRUD grafikte görünür | 3 işlem yap → kova 3 olsun |
| 2 | Toplamlar `psql` sayımıyla birebir | `SELECT COUNT(*) FROM "GatewayAuditLog" WHERE ...` |
| 3 | Dönem değişince sunucuya yeni istek gider | Ağ sekmesi |
| 4 | Yazma olmayan projede açıklayıcı boş durum | Yeni proje |
| 5 | **Hiçbir kutuda kaynağı olmayan sayı yok** | Kod gözden geçirmesi |
| 6 | Başarı oranı hatalı işlemi yansıtır | Kasıtlı hatalı silme → oran düşsün |

Kriter 2, ekranın tamamının güvenilirliğini taşıyor: grafik veritabanı
sayımıyla uyuşmuyorsa geri kalan her şey şüpheli.
