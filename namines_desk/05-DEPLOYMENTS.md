# 05 — Deployments (şema sürüm geçmişi)

> Referans: Vercel **Deployments**. Liste düzeni birebir; "deployment" kavramının
> Namines'teki karşılığı **şema sürümü**.

---

## 1. Kavram eşlemesi

Vercel'de bir deployment = bir git commit'inin build edilip yayına alınması.
Namines'te uygulama yayına alınmıyor — **şema** değişiyor.

| Vercel | Namines karşılığı | Kaynak |
|---|---|---|
| Deployment | Şema sürümü | `SchemaVersion` |
| Commit mesajı | Sürüm mesajı | `SchemaVersion.Message` |
| Branch | Branch | `Branch` (`main` varsayılan) |
| Production / Preview | Varsayılan branch mi, değil mi | `Branch.IsDefault` |
| Status: Ready / Error | İnceleme durumu | `ChangeRequest.Status` |
| Build süresi | — | **karşılığı yok, gösterilmez** |

Bu tabloların hepsi **zaten var** (G10, G11) ve gerçek Postgres'e karşı test
edilmiş durumda — eksik olan yalnızca arayüz.

---

## 2. Liste ekranı

Vercel'in satır düzeni: mesaj · durum · süre · ortam · proje · kaynak · zaman.

Desk'te:

```
fix: müşteri tablosuna telefon alanı      ● Approved   main    v12   2s önce
refactor: sipariş ilişkileri              ● Pending    main    v11   3s önce
feat: ilk şema                            ● Approved   main    v1    dün
```

| Sütun | Kaynak |
|---|---|
| Mesaj | `SchemaVersion.Message` |
| Durum | Bu sürüme bağlı `ChangeRequest.Status` (yoksa "—") |
| Branch | `Branch.Name` + varsayılansa rozet |
| Sürüm | `SchemaVersion.Version` |
| Zaman | `SchemaVersion.CreatedAt` |

Filtreler (Vercel'deki gibi): branch, durum, tarih aralığı.

---

## 3. Sürüm detayı

Tıklayınca:

- **Değişiklik özeti** — `ChangeRequest.ImpactReportJson` içindeki
  `ImpactReport`: eklenen/silinen/değişen tablolar, kırıcı değişiklikler, veri
  kaybı riskleri, kilit riskleri, genel risk seviyesi
- **Onaylar** — `ChangeRequestApproval` kayıtları (kim, ne zaman)
- **Zaman çizelgesi** — `ChangeRequestAuditLog` (Created / AutoApproved /
  Approved / Rejected)

Bunların **hepsi zaten üretiliyor**; ana uygulamada `/review/{id}` ekranında
gösteriliyor. Desk'te aynı veriyi *proje odaklı* bir listeden ulaşılır kılmak
yeni bir hesaplama gerektirmiyor.

> Desk bu ekranda **onay vermeyecek** (v1). Onay, etkisi geri alınamayan bir
> karar ve ana uygulamada kendi ekranı var. İki yerde onaylatmak, hangi
> ekranın yetkili olduğunu bulanıklaştırır.

---

## 4. Kullanıcının istediği "değişiklik olunca uyarı"

> *"naminesten gelen değişiklik olunca uyar gibi bir şey koyucaz, otomatik
> buraya uyarlıcak, branch mantığı gibi düşün"*

Bunu üç ayrı işe ayırmak gerekiyor — hepsi aynı şey değil:

### 4.1 Bildirim (v1'de yapılabilir)

Desk açıkken yeni bir `SchemaVersion` oluşursa üstte bir şerit:
*"Şema v13'e güncellendi — Canvas'ı yenile"*.

Uygulama: **yoklama (polling)**, 30 saniyede bir son sürüm numarasını sor.
WebSocket değil, çünkü SignalR hub'ı şu an canvas işbirliği için
yapılandırılmış; Desk'i ona bağlamak ayrı bir iş.

### 4.2 Şemayı yeniden okuma (v1'de yapılabilir)

Bildirime tıklayınca canvas ve tablo listesi `GET /api/gateway/schema` ile
tazelenir. Zaten var olan uç.

### 4.3 Veritabanına uygulama (v1 DEĞİL) ⚠️

> *"pushla direkt buradaki değişsin"*

Bu, **şema değişikliğini gerçek veritabanında çalıştırmak** demek — yani
`ALTER TABLE`. Bugün Desk'in yaptığı her şey veri satırı seviyesinde; bu ise
yapı seviyesinde ve **geri alınamaz**.

Gerekenler:
- Migration üretimi — `MigrationService` var
- Risk sınıflandırması — `SchemaImpactAnalyzer` var
- Onay zorunluluğu — `ChangeRequestApprovalPolicy` var
- **Yedek** — Vault'un işi, henüz yok

Yani parçaların çoğu duruyor ama **yedeksiz DDL çalıştırmak** kabul edilemez:
`DROP COLUMN` çalıştırıp geri dönemeyen bir kullanıcı ürünü bırakır.

**Karar: bu özellik Vault'tan sonra.** Sırası [`09`](09-YOL-HARITASI.md)'da.

### 4.4 GitHub push (v1 DEĞİL)

`GithubBotService` + webhook altyapısı var ama Desk'e bağlı değil ve
`34-SENDEN-BEKLENENLER.md`'de GitHub App hâlâ 🟡 bekliyor. Hesap olmadan
kodlanacak bir şey yok.

---

## 5. Kabul kriterleri (v1)

| # | Kriter | Kanıt |
|---|---|---|
| 1 | Sürümler gerçek `SchemaVersion` kayıtlarından listelenir | Ana uygulamada "Request Review" → Desk'te satır belirir |
| 2 | Durum rozeti gerçek `ChangeRequest.Status`'u yansıtır | Onayla → rozet değişir |
| 3 | Detayda gerçek `ImpactReport` gösterilir | Kolon silen bir CR → "Breaking" görünür |
| 4 | Branch'i olmayan proje boş durum gösterir, hata vermez | Yeni proje |
| 5 | Yeni sürüm bildirimi gelir | İki sekme: birinde CR aç, diğerinde şerit belirsin |
| 6 | **Hiçbir yerde DDL çalıştırılmaz** | Kod gözden geçirmesi |
