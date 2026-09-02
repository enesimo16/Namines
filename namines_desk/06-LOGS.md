# 06 — Logs

> Referans: Vercel **Logs**. Zaman çizelgesi + filtre paneli + satır listesi
> düzeni alınıyor. İçerik: Namines'in **denetim kaydı**.

---

## 1. ⚠️ Önce dürüstlük: bu "tüm istekler" logu değil

Vercel'in log ekranı her HTTP isteğini gösterir (`GET /finance/expenses 304`).

Namines'te karşılığı **yok**. `GatewayAuditEntry` yalnızca **yazma** işlemlerini
kaydediyor:

```
enum GatewayWriteKind { Create, Update, Delete, Import, Rpc, Sql }
```

Okuma (`list`, `detail`, `schema`) **hiç kaydedilmiyor.**

Bu bilinçli bir tasarım: her okumayı loglamak, veri tarayan bir uygulamada
denetim tablosunu asıl veriden hızlı büyütür.

**Sonuç:** ekranın adı ve boş durumu bunu açıkça söylemeli —
*"Yazma işlemleri"*, "tüm istekler" değil. Aksi hâlde kullanıcı okuma trafiğini
arar ve bulamaz, ürünün bozuk olduğunu sanır.

> Okuma logu istenirse ayrı bir karar: hacim, saklama süresi ve maliyet
> tasarlanmadan eklenmemeli.

---

## 2. Veri kaynağı

`GatewayAuditEntry` — alanları doğrulandı:

| Alan | Ekranda |
|---|---|
| `CreatedAt` | Zaman sütunu |
| `Kind` | İşlem türü rozeti (Create/Update/Delete/Import/Rpc/Sql) |
| `TableName` | Tablo |
| `RowKey` | Etkilenen satır anahtarı |
| `Columns` | Dokunulan kolonlar (virgülle) |
| `AffectedRows` | Etkilenen satır sayısı |
| `Succeeded` | Başarı/hata rozeti |
| `ApiKeyPrefix` | Kaynak: hangi anahtar (dış uygulama) |
| `ActorUserId` | Kaynak: hangi kullanıcı (Desk/Studio) |
| `ProjectId` | **Proje filtresi — bu alan sayesinde ekran mümkün** |

Okuma ucu zaten var: `GET /api/gateway/keys/{projectId}/audit?take=100`
(`AuditTrailAsync`), Admin ve üstü yetki istiyor.

> ⚠️ Mevcut uç **yalnızca `take`** alıyor — tarih aralığı, tür filtresi ve
> sayfalama yok. v1 için genişletilmesi gerekiyor (§4).

---

## 3. Ekran

```
┌─────────────┬────────────────────────────────────────────┐
│ Filtreler   │  [zaman çizelgesi çubuğu]                  │
│ ─────────   ├────────────────────────────────────────────┤
│ Zaman ▾     │ Zaman     Tür     Tablo     Satır   Sonuç  │
│ Tür         │ 18:59:29  UPDATE  customers  id=6   ✓ 1    │
│  ☐ Create   │ 18:55:32  CREATE  customers  id=6   ✓ 1    │
│  ☐ Update   │ 18:51:02  DELETE  vehicles   id=3   ✗ 0    │
│  ☐ Delete   │                                            │
│ Tablo       │                                            │
│ Kaynak      │                                            │
│ Sonuç       │                                            │
└─────────────┴────────────────────────────────────────────┘
```

**"Kaynak" filtresi Vercel'de olmayan ama burada değerli bir eksen:** bir
değişikliği *insan mı* (Desk/Studio, `ActorUserId`) yoksa *bir uygulama mı*
(`ApiKeyPrefix`) yaptı? Olay incelemesinde ilk sorulan soru bu.

Satıra tıklayınca detay: tam kolon listesi, anahtar ön eki, aktör, ham zaman.

---

## 4. Gereken backend değişikliği

`AuditTrailAsync` genişletilmeli:

- `from` / `to` (tarih aralığı)
- `kinds[]` (tür süzgeci)
- `tableName`
- `succeeded` (yalnızca hatalar)
- **sayfalama** — `take=100` bir olay incelemesi için yetersiz

> **Yetki notu:** uç bugün "Admin ve üstü" istiyor, sebebi dokümante edilmiş
> (denetim kaydını okumak, projenin tüm veri hareketlerini görmek demek).
> Desk bu kuralı **gevşetmeyecek**; yetkisi olmayan kullanıcı ekranı görür ama
> "bu bölüm için yönetici yetkisi gerekiyor" der.

---

## 5. Kapsam dışı (v1)

- Canlı akış ("Live" düğmesi) — yoklama ile taklit edilebilir ama olay
  incelemesinde 30 sn gecikme sorun değil; gerçek akış SignalR işi
- Log dışa aktarma — Vercel'de var, burada `export` ucu audit için yok
- Uyarı kuralları (belirli bir olayda bildirim) — ayrı bir alt sistem

---

## 6. Kabul kriterleri

| # | Kriter | Kanıt |
|---|---|---|
| 1 | Desk'ten yapılan bir güncelleme logda belirir | CRUD yap → satır gelsin |
| 2 | Kaynak doğru ayrışır (kullanıcı vs. anahtar) | Bir işlem Desk'ten, bir işlem `curl` + anahtarla |
| 3 | Başarısız işlem ✗ ile görünür | Var olmayan satırı sil |
| 4 | Tarih + tür filtresi sunucuda uygulanır | Ağ isteğinde parametreler görünür |
| 5 | Yetkisiz kullanıcı açıklayıcı mesaj görür, boş liste değil | İkinci hesap |
| 6 | Ekran hiçbir yerde "tüm istekler" demez | Metin gözden geçirmesi |
