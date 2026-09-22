# 29 — Database Change Review ("Database PR")

> GitHub Pull Request'in veritabanı karşılığı. [28-IMPACT-ANALYSIS-ENGINE.md](28-IMPACT-ANALYSIS-ENGINE.md)'in
> çıktısını, teknik olmayan birinin bile "onayla/reddet" diyebileceği tek bir ekranda
> toplar. Bu, Namines'in Claude Code/Codex'ten ayrıldığı somut yüzeydir — agent'lar
> bu onay/kanıt katmanını sağlamıyor ([27 §5](27-LIFECYCLE-PIVOT.md)).

---

## 1. Yaşam döngüsü

```
draft ──▶ analyzing ──▶ pending_review ──▶ approved ──▶ applying ──▶ applied
                              │                              │
                              ▼                              ▼
                          rejected                        failed (otomatik rollback)
```

| Durum | Anlam | Kim tetikler |
|---|---|---|
| `draft` | Branch'te değişiklik yapılıyor | kullanıcı/AI |
| `analyzing` | Impact Analysis çalışıyor | sistem (senkron, <1sn — büyük şemada arka planda) |
| `pending_review` | Rapor hazır, onay bekliyor | sistem |
| `approved` | Onaylandı, uygulanmayı bekliyor | insan (risk seviyesine göre 1 veya 2 kişi) |
| `rejected` | Reddedildi, branch'te kalır | insan |
| `applying` | Migration çalışıyor | worker |
| `applied` | Tamamlandı | worker |
| `failed` | Hata, otomatik rollback denendi | worker |

Bu tablo [18-CONTROL-PLANE-DDL.md](18-CONTROL-PLANE-DDL.md)'deki `migrations` tablosunun
`status` alanına (`planned|approved|running|applied|failed|rolled_back`) doğrudan eşlenir
— yeni tablo gerekmiyor, mevcut şema zaten buna hazır. Eksik olan `pending_review`/
`rejected` durumları ve `analyzing` ara durumu eklenir.

---

## 2. Ekran (tek görünüm, kullanıcının istediği format)

```
┌──────────────────────────────────────────────────────────────┐
│  DATABASE CHANGE REQUEST · #47                                │
│  main ← feature/subscriptions                                 │
├──────────────────────────────────────────────────────────────┤
│  3 tables affected · 2 migrations · 1 new index                │
│  1 potential breaking change                                   │
│                                                                  │
│  Risk: ▓▓▓▓▓▓░░░░ MEDIUM                                       │
├──────────────────────────────────────────────────────────────┤
│  [ Schema Diff ]  [ SQL ]  [ Impact Analysis ]  [ Run Tests ]  │
├──────────────────────────────────────────────────────────────┤
│  (seçili sekmenin içeriği)                                     │
├──────────────────────────────────────────────────────────────┤
│                                    [ Reddet ]  [ Onayla ]      │
└──────────────────────────────────────────────────────────────┘
```

Sekmeler [28 §2](28-IMPACT-ANALYSIS-ENGINE.md)'deki `ImpactReport`'un doğrudan görsel
karşılığıdır — ayrı bir veri modeli icat edilmiyor:

| Sekme | Kaynak |
|---|---|
| Schema Diff | `AffectedTables`/`AffectedRelations` — görsel (canvas'ta kırmızı/yeşil/sarı renkli) |
| SQL | Namines.Compiler'ın ürettiği ham DDL (salt-okunur, kopyalanabilir) |
| Impact Analysis | `BreakingChanges`/`DataLossRisks`/`LockRisks`/`IndexSuggestions` — AI Impact Explainer'ın çevirdiği metin |
| Run Tests | branch DB'de gerçek çalıştırma sonucu (G8.6) — yeşil/kırmızı, hata varsa mesaj |

**UX prensibi ([27 §13](27-LIFECYCLE-PIVOT.md)):** AI burada chat kutusu değil, bir
**durum/aksiyon** yüzeyi. Kullanıcı AI'a soru sormuyor, AI'ın çıkardığı yapıyı okuyup
karar veriyor.

---

## 3. Onay kuralı

[11-MIGRATIONS-BRANCHING.md §6](11-MIGRATIONS-BRANCHING.md)'deki tablo geçerli:

| Risk | Onay gereksinimi |
|---|---|
| Safe | Otomatik onaylanabilir (opt-in ayar) |
| Risky | 1 kişi |
| Destructive / Breaking | 2 kişi (farklı kullanıcılar) |

Onaylayan kişi **değişikliği yapan kişiyle aynı olamaz** destructive/breaking'de —
bu, control DB'de `approved_by char(26)[]` alanının (zaten [18](18-CONTROL-PLANE-DDL.md)'de
var) `author_id`'den farklı olduğu kontrolüyle uygulanır.

---

## 4. "Run Tests" — ne test ediyor

Bu, G5'te kurduğumuz Testcontainers altyapısının **ürün içi, runtime versiyonu**:

```
1. Branch DB'si zaten var (ephemeral veya kalıcı, bkz. 30-SERVER-SIDE-BRANCHING.md)
2. Migration'ı branch DB'sinde GERÇEKTEN çalıştır
3. Başarılıysa: "✔ Migration branch DB'de başarıyla uygulandı"
4. Başarısızsa: motor hatasını OLDUĞU GİBİ göster (G5'te öğrendiğimiz ders —
   "SQL Server üretilen DDL'i reddetti: Msg 1785..." gibi ham, dürüst hata)
5. Opsiyonel: seed veri varsa, migration sonrası satır sayıları korunmuş mu kontrol et
```

Bu adım olmadan "Impact Analysis" sadece bir tahmindir. Bununla **kanıt** olur —
tam olarak G5'in "golden-file metni doğrular, Testcontainers çalıştığını doğrular"
ayrımının ürün seviyesindeki karşılığı.

---

## 5. Bildirim ve mobil (PWA) yüzeyi

[27 §14](27-LIFECYCLE-PIVOT.md)'te tarif edilen mobil kullanım burada somutlaşıyor:

```
Database Change

Risk: MEDIUM

3 tables affected
2 migrations

[Review]  [Approve]
```

Push bildirimi → tıkla → aynı `ImpactReport` verisi, mobilde sadeleştirilmiş (Schema
Diff/SQL sekmeleri masaüstünde; mobilde doğrudan özet + Onayla/Reddet). Aynı API,
farklı render — ayrı bir mobil backend gerekmiyor.

---

## 6. İlişkili doküman güncellemeleri

- [18-CONTROL-PLANE-DDL.md](18-CONTROL-PLANE-DDL.md) `migrations` tablosuna
  `pending_review`/`rejected` durumları eklenmeli (küçük migration, G8'de yapılır)
- [10-REALTIME-COLLAB.md §9](10-REALTIME-COLLAB.md) Namines Bot bölümü — GitHub PR
  entegrasyonu bu ekranın **repo tarafındaki yansıması** olarak kalır, çakışmıyor,
  tamamlıyor (GitHub'da yorum, Namines'te tam ekran review)
