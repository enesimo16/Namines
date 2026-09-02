# 09 — Yol Haritası ve Kabul Kriterleri

> Sıra bağımlılığa göre. Her adımın kabul kriteri **canlı kanıt** ister —
> "test geçti" tek başına yeterli değil.

---

## Sıra

```
D1  Kimlik + kabuk         ← her şey buna bağlı
D2  Projects + Import      ← proje seçilmeden diğer ekranların bağlamı yok
D3  Canvas
D4  Data (v0.1 → v1)
D5  Deployments (okuma)
D6  Logs
D7  Analytics
────────────────────────────
sonra: Vault → ancak ondan sonra "şemayı veritabanına uygula"
```

**Neden bu sıra:** D1 olmadan hiçbir ekran veri çekemez. D2 olmadan diğerleri
hangi projeyi göstereceğini bilemez. D5-D7 birbirinden bağımsız, ama D6 (Logs)
D7'nin (Analytics) veri kaynağını anlamayı gerektiriyor — önce ham kaydı
göstermek, sonra toplamak daha az riskli.

---

## D1 — Kimlik + kabuk

**Kapsam:** giriş formu, JWT, `sessionStorage`, sol gezinme, üst şerit,
proje seçici (boş).

**Backend:** `ResolveConnectionAsync`'e `projectId` + yetki doğrulama dalı
([`01`](01-KIMLIK-VE-OTURUM.md) §3). `GET /api/gateway/schema` oturum yolunu
kabul etsin.

| Kabul kriteri | Kanıt |
|---|---|
| Gerçek hesapla giriş | Tarayıcı |
| **Başkasının projectId'si → 403/404** | İki hesap, `curl` |
| Anahtar girişi arayüzden tamamen kalkmış | Gözden geçirme |

> Ortadaki kriter bu adımın en kritik parçası. Yetki kontrolü unutulursa
> özellik yine "çalışıyor" görünür.

---

## D2 — Projects + Import

**Kapsam:** kart ızgarası, bağlantı durumu, Import akışı, boş durumlar.

**Backend:** `SetProjectConnection` kaydetmeden önce **gerçekten bağlansın**
([`02`](02-PROJECTS.md) §3).

| Kabul kriteri | Kanıt |
|---|---|
| Gerçek projeler kart olarak gelir | Tarayıcı |
| Import → bağlantı **şifreli** saklandı | `psql` ile `v1:` ön eki görülür |
| Yanlış bağlantı reddedilir, sebep yazılır | Tarayıcı |

---

## D3 — Canvas

**Kapsam:** `@xyflow/react` ile salt-okunur şema, `NodePositionsJson`
yerleşimi, FK çizgileri, drift rozeti.

| Kabul kriteri | Kanıt |
|---|---|
| FK çizgileri çizilir | `vehicles → customers` |
| Kullanıcının konumları korunur | Ana uygulamada taşı, Desk'te aynı yer |
| Drift işaretlenir | Elle `ALTER TABLE` → rozet |

---

## D4 — Data v1

**Kapsam:** FK açılır listesi, filtre/sıralama, dışa aktarma.

**Backend:** yok — `GatewayListRequest` bunları zaten destekliyor.

| Kabul kriteri | Kanıt |
|---|---|
| FK okunabilir seçim | "Enes Yel" seçilir, "3" değil |
| 200+ satırda ham girişe düşer | Demo DB'ye 300 satır |
| **v0.1 CRUD bozulmamış** | Ekleme/güncelleme/silme yine `psql` ile doğrulanır |

---

## D5 — Deployments (okuma)

**Kapsam:** sürüm listesi, durum rozetleri, detay (ImpactReport), yeni sürüm
bildirimi (yoklama).

| Kabul kriteri | Kanıt |
|---|---|
| Ana uygulamada CR aç → Desk'te satır belirir | İki sekme |
| Detayda gerçek ImpactReport | Kolon silen CR → "Breaking" |
| **Hiçbir yerde DDL çalıştırılmaz** | Kod gözden geçirmesi |

---

## D6 — Logs

**Kapsam:** denetim kaydı listesi, filtreler, satır detayı.

**Backend:** `AuditTrailAsync` genişletmesi — tarih aralığı, tür, tablo,
başarı, sayfalama ([`06`](06-LOGS.md) §4).

| Kabul kriteri | Kanıt |
|---|---|
| Desk'ten yapılan yazma logda belirir | CRUD yap |
| Kaynak ayrışır (insan vs. anahtar) | Biri Desk'ten, biri `curl` + anahtar |
| Ekran "tüm istekler" demez | Metin gözden geçirmesi |

---

## D7 — Analytics

**Kapsam:** kartlar, zaman serisi, dönem seçici.

**Backend:** `GET /api/gateway/analytics/{projectId}` — SQL'de toplama.

| Kabul kriteri | Kanıt |
|---|---|
| Toplamlar `psql` sayımıyla birebir | `SELECT COUNT(*) …` |
| Yazma olmayan projede açıklayıcı boş durum | Yeni proje |
| **Kaynağı olmayan sayı yok** | Kod gözden geçirmesi |

---

## Bilinçli olarak v1 dışı

| Ne | Neden | Nereye |
|---|---|---|
| Şemayı veritabanına uygulama (DDL) | Yedek olmadan geri alınamaz | Vault'tan sonra |
| GitHub push entegrasyonu | GitHub App hesabı 🟡 bekliyor | `34-SENDEN-BEKLENENLER` |
| Okuma logu | Hacim/saklama/maliyet tasarlanmadı | Ayrı karar |
| Proje bazlı kullanım/fatura | `UsageEvent`'te `ProjectId` yok, faturayı etkiler | Ayrı karar |
| API anahtarı yönetim ekranı | Desk artık oturumla çalışıyor; anahtarlar dış uygulamalar için | v1.1 |
| SSO devri | Yeni uç + jeton ömrü tasarımı | v1.1 |
| Canlı log akışı | SignalR'a bağlanmak ayrı iş | v1.1 |

---

## Her adımda geçerli kurallar

1. **Yürüyen iskelet önce.** Tek uçtan uca yol kanıtlanmadan ikinci özellik yok.
2. **Kanıt = çalışan komut + görülen çıktı.** Veri değiştiren her kriter
   `psql` ile bağımsız doğrulanır — API'nin kendi cevabına güvenilmez.
3. **Regresyon.** Her adımda `dotnet test` (şu an 1356) ve Desk `tsc` temiz.
4. **Mikroservis sınırı.** `Namines.Core`/`frontend`'e referans yok. npm
   paketleri (React Flow, dagre, grafik) serbest — onlar proje kodu değil.
5. **Uydurma veri yok.** Kaynağı olmayan kutu çizilmez.
