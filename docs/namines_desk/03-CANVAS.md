# 03 — Canvas (proje şeması görünümü)

> Kullanıcı bir projeye tıkladığında ilk gördüğü ekran: **neyin ne olduğunu**
> gösteren şema haritası.

---

## 1. Amaç ve sınır

Bu **salt-okunur** bir görünüm. Şema düzenleme ana uygulamanın işi; Desk'te
tabloları düzenlemek iki ayrı yerde iki ayrı doğruluk kaynağı yaratırdı.

Desk'teki canvas'ın işi tek: *"bu veritabanında ne var, neyle neye bağlı"*
sorusunu bir bakışta cevaplamak — sonra kullanıcı bir tabloya tıklayıp
verisine geçsin ([`04`](04-DATA-CRUD.md)).

---

## 2. Veri kaynağı — ve kritik bir seçim

İki farklı kaynak var ve **aynı şeyi göstermiyorlar**:

| Kaynak | Ne anlatır | Konum bilgisi |
|---|---|---|
| `CloudProject.SchemaJson` | Kullanıcının canvas'ta **çizdiği tasarım** | `NodePositionsJson` ile birlikte **var** |
| `GET /api/gateway/schema` | Veritabanının **gerçek** hâli (canlı introspection) | yok |

Bunlar **ayrışmış olabilir**: kullanıcı elle `ALTER TABLE` çalıştırmış, başka
bir araç kolon eklemiş olabilir.

### Karar: ikisini de göster, farkı işaretle

- **Yerleşim** `NodePositionsJson`'dan gelir — kullanıcının kendi düzeni korunur
- **İçerik** canlı şemadan gelir — gerçek kolonlar gösterilir
- **Fark varsa görünür olur:**
  - tasarımda var, veritabanında yok → tablo soluk + "veritabanında yok"
  - veritabanında var, tasarımda yok → yeni düğüm, "tasarımda yok" rozetiyle,
    otomatik yerleştirilir

Bu, Desk'e ana uygulamanın vermediği bir değer katıyor: **drift tespiti.**
Tek bir kaynağa yaslanmak ya yanlış kolonları gösterirdi (tasarım) ya da
kullanıcının düzenini kaybederdi (canlı).

> Yeni düğümlerin konumu için ana uygulamanın `lib/autoLayout.ts`'i
> **kopyalanmayacak** — mikroservis sınırı. Desk kendi basit yerleştirmesini
> yapar (ızgara), ya da `@dagrejs/dagre`'ı kendi bağımlılığı olarak kurar.
> Dagre bir npm paketi, proje kodu değil; onu kullanmak sınırı ihlal etmez.

---

## 3. Render

`@xyflow/react` — ana uygulamanın da kullandığı **npm paketi**. Aynı paketi
kullanmak kod paylaşmak değildir; `TableNode.tsx` gibi bileşenler
kopyalanmayacak, Desk kendi düğümünü yazacak (daha basit: salt-okunur, düzenleme
tutamacı yok).

### Düğüm içeriği

```
┌─────────────────────────┐
│ users            4 cols │  ← başlık: ad + kolon sayısı
├─────────────────────────┤
│ 🔑 id              INT  │  ← PK ikonu
│    email       VARCHAR  │
│ 🔗 role_id         INT  │  ← FK ikonu
│    created_at TIMESTAMP │
└─────────────────────────┘
```

FK bağlantıları `GET /api/gateway/schema`'nın `references` alanından çizilir —
bu alan v0.1'de eklendi ve **canlı doğrulandı**
([third-phase §2](../third-phase/00-BASLA-BURADAN.md)).

---

## 4. Etkileşim

| Eylem | Sonuç |
|---|---|
| Tabloya tıkla | Sağ panelde tablo özeti (kolonlar, tipler, ilişkiler) |
| Tabloya çift tıkla | **Data sekmesine** geç, o tablo seçili ([`04`](04-DATA-CRUD.md)) |
| Boşluğa tıkla | Seçimi bırak |
| Sürükle / zoom | Yalnızca görüntüleme — **konum kaydedilmez** |

Son satır bilinçli: Desk'te düzeni kaydetmek, ana uygulamadaki düzenle
çakışırdı. Hangisi doğru olurdu? Cevabı olmayan bir soru yaratmamak için
Desk hiç kaydetmiyor.

---

## 5. Kapsam dışı (v1)

- Şema düzenleme (ana uygulamanın işi)
- Konum kaydetme (yukarıdaki gerekçe)
- Minimap / otomatik yerleşim düğmesi — 30+ tabloda gerekli olur, v1'de değil

---

## 6. Kabul kriterleri

| # | Kriter | Kanıt |
|---|---|---|
| 1 | Gerçek şema canvas'ta çizilir, kolonlarla | Tarayıcı, `desk_demo` |
| 2 | FK ilişkileri çizgi olarak gelir | `vehicles.customer_id → customers.id` görünür |
| 3 | Kullanıcının kaydettiği konumlar korunur | Ana uygulamada taşı → Desk'te aynı yerde |
| 4 | **Drift işaretlenir** | Elle `ALTER TABLE ... ADD COLUMN` → Desk'te rozet |
| 5 | Çift tıklama Data sekmesine götürür | Tarayıcı |

Kriter 4'ün testi elle bir DDL çalıştırmayı gerektiriyor — bu, Desk'in
*demo veritabanında* yapılır, kullanıcının veritabanında değil.
