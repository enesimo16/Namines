# 10 — Gerçek Zamanlı İşbirliği

> Faz 1 sorunu: `CanvasHub` bir **broadcast rölesiydi**. Sunucuda şema state'i yoktu, son-yazan-kazanır mantığı veri kaybettiriyordu, oda üyeliği `static ConcurrentDictionary` içindeydi (yani ikinci bir instance açıldığı an multiplayer bölünür), ve hub'da kimlik doğrulama yoktu.

---

## 1. CRDT'ye geçiş (Yjs)

| | Faz 1 | Faz 2 |
|---|---|---|
| Model | Tam şema broadcast | Yjs `Y.Doc` — operasyon tabanlı CRDT |
| Çakışma | Son yazan kazanır (veri kaybı) | **Matematiksel olarak çakışmasız** |
| Bant genişliği | Her değişiklikte tüm şema | Sadece delta (bytes) |
| Offline | ✖ | ✔ (IndexedDB, sonradan senkron) |
| Undo/redo | 50 snapshot yığını, tek kullanıcı | `Y.UndoManager`, kullanıcı-farkında, sınırsız |
| Sunucu state | Yok | Yjs dokümanı Redis + Postgres'te kalıcı |

### Doküman yapısı

```js
ydoc
 ├─ tables:    Y.Map<tableUuid, Y.Map>       // her tablo bir Y.Map
 │    └─ columns: Y.Array<Y.Map>             // sıra korunur
 ├─ relations: Y.Map<fkUuid, Y.Map>
 ├─ enums:     Y.Map
 ├─ layout:    Y.Map<tableUuid, {x,y,color}> // pozisyonlar (sık değişir, ayrı tutulur)
 ├─ notes:     Y.Array
 └─ meta:      Y.Map                          // ad, engine, naming
```

`layout` ayrı tutulur çünkü sürükleme sırasında saniyede onlarca güncelleme üretir; bunlar kalıcı sürüm geçmişine yazılmaz.

---

## 2. Sunucu tarafı

```
namines-realtime (.NET 10)
 ├─ SignalR Hub: /hubs/canvas
 ├─ Redis backplane (Microsoft.AspNetCore.SignalR.StackExchangeRedis)
 ├─ Yjs sunucu tarafı: ycs (C# port) veya Node sidecar (y-websocket)
 ├─ Kalıcılık: her 5 sn veya 100 op'ta bir Postgres'e snapshot
 └─ Presence: Redis, TTL 30 sn, heartbeat
```

**Karar:** Yjs'in C# portu (`Ycs`) olgunluk açısından riskli. **Alternatif ve önerilen:** `y-websocket` uyumlu hafif bir Node sidecar (`namines-yjs`, ~200 satır) + kalıcılık için Namines API'sine webhook. Bu, .NET tarafında CRDT implementasyonu yazma riskini ortadan kaldırır.

---

## 3. Kimlik doğrulama ve yetkilendirme (Faz 1'in en büyük açığı)

```
Bağlantı: wss://rt.namines.com/hubs/canvas?access_token=...
 ├─ 1. Token doğrula (JWT veya paylaşım linki token'ı)
 ├─ 2. Bu kullanıcı bu projeye erişebilir mi? → control plane'e sor (60 sn cache)
 ├─ 3. Yazma yetkisi var mı? (viewer ise read-only Y.Doc)
 ├─ 4. Odaya kat: room = "prj_{id}:branch_{name}"
 └─ 5. Her operasyon yetkiye göre filtrelenir (viewer'ın op'ları reddedilir)
```

Faz 1'deki "tahmin edilemez roomId = capability" modeli kaldırılır. Paylaşım linkleri hâlâ çalışır ama artık **sunucuda kayıtlı, süreli, iptal edilebilir** token'lardır.

| Faz 1 açığı | Faz 2 kapanışı |
|---|---|
| Hub'da auth yok, herkes JoinRoom çağırabilir | Bağlantıda JWT/token zorunlu |
| roomId sızarsa süresiz erişim | Token süreli + iptal edilebilir + audit'li |
| Viewer da şema gönderebilir | Rol bazlı op filtreleme |
| Cross-room enjeksiyon (kısmen korunmuş) | Yapısal olarak imkânsız (Y.Doc oda başına) |

---

## 4. Presence

```jsonc
{
  "connectionId": "cn_...",
  "userId": "usr_...",
  "displayName": "Ayşe",
  "avatarUrl": "...",
  "color": "#f59e0b",          // deterministik, userId hash'inden
  "cursor": { "x": 420, "y": 180 },
  "selection": { "tableUuid": "9c2e...", "columnUuid": null },
  "viewport": { "x": 0, "y": 0, "zoom": 0.8 },
  "status": "active",           // active | idle | away
  "lastSeen": "2026-08-08T12:00:00Z"
}
```
- İmleç güncellemeleri throttle 50 ms, Y.Doc'a değil **awareness** kanalına gider (kalıcı değil)
- Faz 1'deki `MultiplayerCursors` bileşeni korunur, awareness API'sine bağlanır
- "Ayşe şu an `orders` tablosunu düzenliyor" göstergesi

---

## 5. Branch'ler (sunucu tarafına taşındı)

> Faz 1 README'si branch'lerin "cihaz başına yerelde" tutulduğunu itiraf ediyordu — bu sürüm kontrolü değildi.

```
Proje
 ├─ main                    → Y.Doc + kalıcı sürümler
 ├─ feature/orders-v2       → main'den çatallandı (v43'te)
 └─ preview/pr-142          → otomatik, PR'a bağlı
```

| İşlem | Davranış |
|---|---|
| Branch oluştur | Y.Doc snapshot kopyalanır + (varsa) Neon DB branch'i açılır |
| Branch'e geç | Farklı Y.Doc odasına bağlan |
| Diff | `NslDiffer.Diff(mainDoc, branchDoc)` → görsel karşılaştırma |
| Merge | 3-yollu birleştirme (`NslMerger.ThreeWay`) — Faz 1'deki 3-way merge mantığı **korunur**, sunucuya taşınır |
| Çakışma | `ConflictResolverModal` (Faz 1 bileşeni korunur) — alan bazlı seçim |
| Kapat | Y.Doc arşivlenir, DB branch'i silinir |

**Çakışma türleri ve otomatik çözüm:**

| Tür | Otomatik çözülür mü |
|---|---|
| İki tarafta farklı tablo eklendi | ✔ ikisi de |
| Aynı tabloya farklı kolonlar eklendi | ✔ ikisi de |
| Aynı kolonun tipi iki tarafta değişti | ✖ kullanıcı seçer |
| Bir tarafta silinen tablo diğerinde düzenlendi | ✖ kullanıcı seçer |
| Sadece pozisyon değişti | ✔ (layout çakışması önemsiz) |
| Aynı ada sahip iki yeni tablo | ✖ yeniden adlandırma önerilir |

---

## 6. Sürüm geçmişi

```
GET /v1/projects/{id}/versions
→ [{ version: 47, checksum, author, message, createdAt, opCount, tableCount }]
```
- Her anlamlı değişiklik grubu (30 sn hareketsizlik veya açık "kaydet") bir sürüm oluşturur
- Sürümler **immutable**, checksum'lı
- Zaman tüneli UI: kaydırıcıyla geçmişe git, önizle, geri yükle (yeni sürüm olarak — geçmiş silinmez)
- Her sürümün NSL'i S3'te sıkıştırılmış tutulur; Postgres'te sadece metadata

---

## 7. Yorumlar (yeni)

```jsonc
{
  "id": "cmt_...",
  "anchor": { "type": "column", "tableUuid": "...", "columnUuid": "..." },
  "body": "Bu alan gerçekten nullable olmalı mı? @mehmet",
  "author": "usr_...",
  "mentions": ["usr_..."],
  "resolved": false,
  "thread": [ ... ],
  "createdAt": "..."
}
```
Tablo/kolon/ilişki/canvas noktasına iliştirilir. E-posta + Slack bildirimi. Şema değişse bile UUID sayesinde yorum yerinde kalır.

---

## 8. Ölçekleme

| Boyut | Sınır | Strateji |
|---|---|---|
| Oda başına eşzamanlı kullanıcı | 50 | Awareness throttling, delta batching |
| Instance başına oda | ~5.000 | Yatay ölçekleme + Redis backplane |
| Y.Doc boyutu | ~10 MB | Periyodik `Y.encodeStateAsUpdate` sıkıştırma + geçmiş budama |
| Mesaj hızı | 100 msg/sn/oda | Sunucu tarafı throttle |
| Yeniden bağlanma | — | Exponential backoff + offline kuyruğu (Y.Doc zaten dayanıklı) |

---

## 9. Namines Bot (GitHub App) — işbirliğinin git tarafı

Faz 1'deki `scripts/namines-diff.mjs` ve schema-diff workflow'u **korunur** ve GitHub App'e terfi eder.

**PR'da yaptığı:**
```markdown
## 🗄️ Namines Schema Review

**3 değişiklik** · main → feature/orders-v2 · şema v43 → v47

| Risk | Operasyon | Detay |
|---|---|---|
| 🟢 safe | `+ orders.currency` | nullable, varsayılan 'TRY' |
| 🟡 risky | `~ orders.total` numeric(10,2) → numeric(12,2) | ACCESS EXCLUSIVE lock ~4.2 sn (128k satır) |
| 🔴 destructive | `- users.legacy_field` | **128.443 satırda veri kalıcı silinecek** |

**Önizleme veritabanı:** `br-pr-142` ([Console'da aç](https://console.namines.com/...))
**Rollback script'i:** [indir](...)
**Etkilenen istemci tipleri:** `Database['users']` kırılıyor — 3 dosyada derleme hatası bekleniyor

<sub>Bu kontrolü geçmek için bir maintainer `/namines approve` yazmalı.</sub>
```

- Status check olarak PR'ı bloke edebilir
- `.nsl` dosyası repoda tutulur, iki yönlü senkron
- Merge sonrası main branch'e migration otomatik uygulanabilir (opt-in)

**Neden önemli:** Bu, ürünü geliştiricinin **günlük iş akışına** sokar. Retention'ın ikinci ayağı (birincisi Console).
