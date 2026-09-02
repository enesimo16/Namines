# 02 — Projects (ana ekran)

> Referans: Vercel **Overview**. Düzen birebir alınıyor; içerik Namines'in
> gerçek verisi.

---

## 1. Düzen

Vercel'in Overview'ı üç bölgeden oluşuyor:

```
┌────────────┬──────────────────────────────────────────────┐
│            │  [Tüm Projeler ▾]        Overview      [+]   │  ← üst şerit
│  Projects  ├───────────────────┬──────────────────────────┤
│  Deploy…   │                   │  Projects                │
│  Logs      │   Usage           │  ┌────────┐ ┌────────┐    │
│  Analytics │   (özet kutusu)   │  │ kart   │ │ kart   │    │
│  …         │                   │  └────────┘ └────────┘    │
│            │   Alerts          │  ┌────────┐ ┌────────┐    │
│  ─────     │                   │  │ kart   │ │ kart   │    │
│  [kullanıcı]│  Recent Previews │  └────────┘ └────────┘    │
└────────────┴───────────────────┴──────────────────────────┘
```

Sol sütun **kalıcı** (her ekranda aynı), orta sütun özet, sağ sütun kart ızgarası.

### Namines karşılığı

| Vercel bölgesi | Desk'te ne olacak |
|---|---|
| Sol gezinme | Projects · Deployments · Logs · Analytics · Settings |
| Üst proje seçici | Proje seçili değilken "All Projects", seçiliyken proje adı |
| **Usage kutusu** | ⚠️ Vercel'in metrikleri yok. Yerine: proje sayısı, bağlı veritabanı sayısı, son 30 gün yazma işlemi sayısı ([`07`](07-ANALYTICS.md)) |
| **Alerts kutusu** | Bağlantısı olmayan / erişilemeyen projeler uyarısı |
| **Recent Previews** | Son şema sürümleri ([`05`](05-DEPLOYMENTS.md)) |
| Proje kartları | `GET /api/auth/projects` |
| **"New repository detected → Import"** | **Namines projelerini içe aktarma** (§3) |

---

## 2. Proje kartı

Vercel'in kartı: ikon, ad, alan adı, son commit mesajı, repo + zaman, durum rozeti.

Desk'in kartı — **her alanın gerçek bir kaynağı var**:

| Alan | Kaynak |
|---|---|
| Ad | `CloudProject.Name` |
| Motor rozeti | `CloudProject.DbType` |
| Tablo sayısı | `SchemaJson` içinden sayılır |
| Bağlantı durumu | `EncryptedConnectionString` dolu mu → 🟢 bağlı / ⚪ bağlı değil |
| Son değişiklik | `CloudProject.UpdatedAt` |
| Sahip | `ownerName` (`/api/auth/projects` döndürüyor) |

> **Vercel'deki "son commit mesajı" karşılığı** `SchemaVersion.Message` olabilir
> — ama yalnızca projenin sunucu tarafında bir branch'i varsa var
> ([`05`](05-DEPLOYMENTS.md)). Yoksa alan **gösterilmez**, uydurulmaz.

Karta tıklama → proje detayına gider, varsayılan sekme **Canvas**
([`03`](03-CANVAS.md)).

---

## 3. "Import" — Namines projelerini içe aktarma

Vercel'de bu, GitHub deposunu Vercel projesine bağlamak demek.

**Desk'te ne demek:** kullanıcının Namines projesi zaten var (aynı hesap, aynı
control DB). Desk'in "içe aktarması" gereken tek şey **canlı veritabanı
bağlantısı** — çünkü Desk veriyi yönetir, veri de o bağlantının ucunda.

Yani Import akışı:

```
1. Bağlantısı olmayan projeler "Import" rozetiyle listelenir
2. Tıkla → bağlantı dizesi + motor sor
3. PUT /api/gateway/keys/project/{id}/connection   (şifreli saklanır)
4. Doğrula: GET /api/gateway/schema → tablolar gelirse başarılı
5. Kart 🟢 "bağlı" olur
```

**Adım 4 atlanamaz.** Bağlantıyı kaydedip "başarılı" demek, ilk veri ekranında
patlaması demek olurdu. Kaydetme anında bir kez gerçekten bağlanmak, hatayı
kullanıcının onu düzeltebileceği ana taşır.

> Bugün `SetProjectConnection` yalnızca SSRF kontrolü yapıyor, **bağlanmayı
> denemiyor**. v1'de bağlantı testi eklenecek.

---

## 4. Boş durumlar

| Durum | Ne gösterilir |
|---|---|
| Hiç proje yok | "Namines'te henüz proje yok" + ana uygulamaya bağlantı |
| Projeler var, hiçbiri bağlı değil | Kartlar + belirgin "Import" çağrısı |
| Bağlantı var ama şema okunamıyor | Kart üzerinde hata rozeti + sunucunun **ham** mesajı |

Son satır önemli: "bir hata oluştu" demek, kullanıcıyı yanlış kimlik bilgisi ile
erişilemeyen sunucu arasında kör bırakır. Gateway zaten anlamlı mesajlar
döndürüyor; onları taşı.

---

## 5. Kapsam dışı (v1)

- Proje oluşturma/silme — ana uygulamanın işi, Desk'in değil
- Takım/üye yönetimi — `ProjectMemberController` var ama ayrı bir ekran
- Arama/filtreleme — 5-10 projede gereksiz; kart sayısı artınca eklenir

---

## 6. Kabul kriterleri

| # | Kriter | Kanıt |
|---|---|---|
| 1 | Giriş sonrası gerçek projeler kart olarak gelir | Tarayıcı |
| 2 | Bağlı/bağlı değil rozeti doğru | İki proje, biri bağlantılı |
| 3 | Import → bağlantı kaydedilir **ve doğrulanır** | Tarayıcı + `psql` ile şifreli saklandığı teyidi |
| 4 | Yanlış bağlantı dizesi → kaydedilmez, sebep gösterilir | Tarayıcı |
| 5 | Karta tıklama → canvas | Tarayıcı |
