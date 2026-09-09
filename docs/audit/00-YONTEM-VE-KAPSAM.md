# 00 — Yöntem, kapsam ve bu denetimin sınırları

**Tarih:** 2026-09-09
**Denetlenen sürüm:** `47421e9` (main, origin ile eşit)
**Kapsam:** `backend/` (~62k satır C#), `frontend/` (~32k satır TS/TSX),
`services/desk/` (~6k satır), `docker-compose.yml`, `.github/workflows/`, dokümanlar.

---

## Kanıt seviyeleri

Bu denetim, deponun kendi disiplinini kullanıyor (bkz. `DURUM.md`):

| Seviye | Anlamı |
|---|---|
| ✅ **Ölçüldü** | Komut çalıştırıldı, çıktısı görüldü ve rapora alındı |
| 🟡 **Kod okundu** | Kaynak incelendi, davranış çalıştırılarak doğrulanmadı |
| ⚠️ **Doğrulanmadı** | İddia var, bu oturumda kanıtlanamadı — sebebi yazılı |

Her bulgunun yanında dosya ve satır var. Satırsız bulgu yok.

## Bu denetimin YAPMADIKLARI — dürüstçe

1. **Kod değiştirilmedi.** Talimat gereği yalnızca keşif/analiz yapıldı.
2. **Piyasa araştırması yapılamadı.** Web arama ve sayfa getirme araçları bu
   oturumda model yönlendirme hatası verdi. Bu yüzden `16-competitor-analysis.md`
   ve `17-feature-comparison.md` içindeki **her fiyat ve özellik iddiası
   DOĞRULANMAMIŞ** olarak işaretlendi; uydurulmadı, teyit listesi bırakıldı.
3. **Canlı sızma testi yapılmadı.** Bulgular kod okumasından; istismar
   denenmedi. "Saldırı senaryosu" alanları teorik ama koda dayalı.
4. **Uygulama bu denetim için ayağa kaldırılmadı** (aynı oturumda daha önce
   kaldırıldı ve Vault uçları canlı çalıştırıldı; UI akışları tıklanmadı).
   UI/UX bulguları kod okumasına dayanıyor — bu bir sınırlamadır.
5. **Yük/performans ölçümü yapılmadı.** Performans bulguları yapısal.

## Okuma sırası

Aceleniz varsa: `01-executive-summary.md` → `27-final-verdict.md` → `23-quick-wins.md`.
