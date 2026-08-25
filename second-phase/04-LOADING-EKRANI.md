# 04 — Üretim Ekranı (loading değil, kanıt akışı)

> **Sıra: 1.** En ucuz iş, en yüksek etki. Diğer her şeyden önce bu.

---

## Ne

Prompt ile canvas arasına, üretimin **adım adım ne yaptığını** gösteren bir ekran.
Bir "yükleniyor" dönen çarkı değil — çalışan hattın canlı raporu.

```
✓ İş türü tanındı — E-ticaret
✓ Taslak üretildi — 7 tablo, 9 ilişki
⟳ PostgreSQL'de derleniyor…
⚠ 2 bulgu: orders.user_id yabancı anahtarı eksik
⟳ Düzeltiliyor (tur 2/3)…
✓ 6 motorun hepsinde derleniyor
```

## Neden bu ilk sırada

Ürünün en özgün tarafı **görünmez**: AI şema üretiyor, sonra kural motoru ve
6 gerçek DDL derleyicisi onu denetleyip düzelttiriyor. Kullanıcı bunların
hiçbirini görmüyor — sadece sonucu görüyor, o da ChatGPT'nin verdiğine benziyor.

Bu ekran, *"biz de AI ile şema yapıyoruz"*u **"biz kanıtlıyoruz"a** çeviriyor.
Tek bir kullanıcı görüşmesi yapmadan, canlıya çıkmadan farkı anlatıyor.

**Veri zaten var:** `SchemaAgentResult` içinde `Rounds`, `RemainingFindings`,
`PortabilityNotes` hesaplanıyor. Eksik olan tek şey bunları **akış hâlinde**
göstermek — bugün hepsi iş bitince tek seferde dönüyor.

## Nasıl

1. `SchemaAgentPipeline`'a adım bildirimi ekle (`IProgress<AgentStep>` ya da SSE)
2. `/api/schema/generate` akışlı yanıt versin (Server-Sent Events)
3. Ön yüzde adımlar geldikçe listeye eklensin

Ara adım gösterilemiyorsa **uydurma** — gerçekten bilinen adımları göster.

## ⚠️ Dikkat

- **Sahte ilerleme çubuğu yok.** Yüzde uydurmak, ilk yanlış tahminde güveni
  bitirir. Adım listesi yüzdeden dürüsttür.
- **Hata da gösterilmeli.** "2 bulgu düzeltildi" yazmak, işin çalıştığının
  kanıtı — gizlemek, ekranı reklama çevirir.
- Kullanıcı ekranı **kapatabilmeli**; uzun süren bir üretimde hapsolmamalı.
- `prefers-reduced-motion`: buradaki animasyonlar UI animasyonu, arka plan
  kimliği değil — bu ekranda medya sorgusuna **saygı gösterilmeli**.

## 🔴 Yapılmayacak

- Süslü, uzun animasyonlar. Ekran işi **bittiğinde kapanmalı**; kullanıcıyı
  animasyon bitsin diye bekletmek, hızlı bir ürünü yavaş göstermek olur.
- Adımları yapay olarak yavaşlatmak ("daha çok çalışıyor görünsün" diye).
