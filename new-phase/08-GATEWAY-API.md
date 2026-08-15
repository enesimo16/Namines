# 08 — Namines Gateway (Otomatik API)

Şemadan türetilen, çalışma zamanında yaşayan REST + GraphQL API. Console'un veri kaynağı ve müşterinin uygulamasının backend'i.

---

## 1. Temel prensipler

1. **Sıfır konfigürasyon** — tablo eklendiği an endpoint canlı
2. **Sıfır kod üretimi** — Gateway metadata okur, endpoint'i runtime'da çözer
3. **Güvenli varsayılan** — hiçbir tablo varsayılan olarak public değil
4. **Parametreli SQL her zaman** — string birleştirme yok, injection yüzeyi yok
5. **Sözleşme kararlı** — şema değişince kırıcı değişiklikler sürümlenir

---

## 2. REST yüzeyi

Taban: `https://api.namines.com/v1/{projectSlug}`

| Metot | Yol | Açıklama |
|---|---|---|
| `GET` | `/tables` | Erişilebilir tablo listesi (role göre) |
| `GET` | `/tables/{table}` | Tablo metadata'sı (kolonlar, tipler, ilişkiler) |
| `GET` | `/tables/{table}/rows` | Liste (filtre/sırala/sayfala) |
| `GET` | `/tables/{table}/rows/{pk}` | Tek kayıt |
| `POST` | `/tables/{table}/rows` | Oluştur (tekil veya toplu) |
| `PATCH` | `/tables/{table}/rows/{pk}` | Kısmi güncelleme |
| `PUT` | `/tables/{table}/rows/{pk}` | Tam değiştirme |
| `DELETE` | `/tables/{table}/rows/{pk}` | Sil (soft-delete varsa onu kullanır) |
| `GET` | `/tables/{table}/rows/{pk}/{relation}` | İlişkili kayıtlar |
| `POST` | `/tables/{table}/bulk` | Toplu upsert |
| `GET` | `/tables/{table}/count` | Sayım (filtreli) |
| `GET` | `/tables/{table}/export` | CSV/JSON/Parquet dışa aktarım (stream) |
| `POST` | `/tables/{table}/import` | CSV/JSON içe aktarım |
| `POST` | `/rpc/{function}` | Tanımlı SQL fonksiyonu çağır |
| `POST` | `/query` | Ham SQL (sadece `sql:execute` scope'lu anahtar) |
| `POST` | `/query/nl` | Doğal dil → SQL → sonuç |
| `GET` | `/openapi.json` | OpenAPI 3.1 spesifikasyonu |
| `GET` | `/health` | Sağlık |
| `POST` | `/graphql` | GraphQL endpoint |
| `WS` | `/realtime` | Değişiklik akışı (P2) |

### 2.1 Sorgu dili

```
GET /v1/shopfront/tables/orders/rows
  ?select=id,total,status,user_id
  &user_id=eq.9c2e-...
  &total=gte.100&total=lt.5000
  &status=in.(paid,shipped)
  &placed_at=gte.2026-01-01
  &search=ahmet                      // full-text, aranabilir kolonlarda
  &order=placed_at.desc,id.asc
  &limit=50&offset=100
  &expand=user,items                 // ilişkileri gömer (N+1 yok, tek sorgu)
  &count=exact                       // exact | estimated | none
```

**Operatörler:** `eq` `neq` `gt` `gte` `lt` `lte` `like` `ilike` `in` `nin` `is` (null/true/false) `between` `contains` (array/json) `overlaps` `fts` (full-text)

**Mantıksal gruplama:** `?or=(status.eq.paid,total.gte.1000)`

> Sözdizimi bilinçli olarak PostgREST/Supabase'e benzer — geliştiricinin öğrenme maliyetini sıfırlar ve göç kolaylaştırır.

### 2.2 Yanıt zarfı

```jsonc
{
  "data": [ { "id": 1, "total": "129.90", "user": { "id": "...", "display_name": "Ayşe" } } ],
  "meta": {
    "count": 1284,
    "limit": 50,
    "offset": 100,
    "schemaVersion": 47,
    "durationMs": 12
  },
  "links": { "next": "...offset=150", "prev": "...offset=50" }
}
```

Hata:
```jsonc
{
  "error": {
    "code": "CONSTRAINT_VIOLATION",
    "message": "orders.total 0'dan küçük olamaz",
    "detail": { "constraint": "ck_orders_total", "table": "orders", "column": "total" },
    "requestId": "req_01J..."
  }
}
```
DB hata mesajları **ham gösterilmez** — NSL constraint tanımından insan-okunur mesaja çevrilir. (Bu, `@ui` katmanının bir yan faydasıdır.)

---

## 3. GraphQL

Şemadan otomatik türetilir:

```graphql
type Order {
  id: BigInt!
  status: OrderStatus!
  total: Decimal!
  placedAt: DateTime!
  user: User!                       # FK'dan
  items(limit: Int, offset: Int): [OrderItem!]!   # ters FK'dan
}

type Query {
  order(id: BigInt!): Order
  orders(where: OrderFilter, orderBy: [OrderOrder!], limit: Int, offset: Int): OrderConnection!
  ordersAggregate(where: OrderFilter): OrderAggregate!
}

type Mutation {
  createOrder(input: OrderCreateInput!): Order!
  updateOrder(id: BigInt!, input: OrderUpdateInput!): Order!
  deleteOrder(id: BigInt!): Boolean!
}
```

- **DataLoader** ile N+1 önleme
- Sorgu derinliği ve karmaşıklık limiti (DoS koruması)
- Introspection sadece kimlik doğrulanmış istemcilere
- Persisted queries (P2)

---

## 4. Kimlik doğrulama ve yetkilendirme

### 4.1 Üç istemci tipi

| Tip | Kimlik | Kullanım |
|---|---|---|
| **API Key** | `Authorization: Bearer nam_live_...` | Sunucu-sunucu |
| **Console kullanıcısı** | JWT (Console oturumu) | Admin panel |
| **Son kullanıcı JWT** | Müşterinin kendi auth sistemi (JWKS ile doğrulanır) | Müşterinin uygulaması |

### 4.2 Katmanlı yetkilendirme

```
İstek
 ├─ 1. Kimlik: anahtar/token geçerli mi? → 401
 ├─ 2. Scope: anahtarın bu operasyona izni var mı? (`orders:read`) → 403
 ├─ 3. Rol izinleri: rolün bu tabloya/kolona erişimi var mı? → 403 / kolon kırpma
 ├─ 4. Satır filtresi: rolün rowFilter'ı WHERE'e eklenir
 ├─ 5. RLS: DB bağlantısında `SET LOCAL app.claims = '{...}'` → PG politikaları uygular
 └─ 6. Rate limit: kiracı + anahtar bazlı → 429
```

**İki katmanlı savunma bilinçli:** Uygulama seviyesi filtre (hızlı, açıklanabilir) + DB seviyesi RLS (Gateway'de bug olsa bile veri sızmaz).

### 4.3 API anahtarı modeli

```jsonc
{
  "id": "key_01J...",
  "name": "Production backend",
  "prefix": "nam_live_a1b2",            // gösterilebilir kısım
  "hash": "argon2id$...",               // tam anahtar sadece bir kez gösterilir
  "scopes": ["orders:read", "orders:write", "users:read"],
  "roleName": "service",
  "environment": "production",
  "allowedIps": ["1.2.3.4/32"],
  "allowedOrigins": ["https://app.musteri.com"],
  "rateLimit": { "rpm": 600, "burst": 60 },
  "expiresAt": "2027-01-01T00:00:00Z",
  "lastUsedAt": "2026-08-08T12:00:00Z"
}
```

---

## 5. Rate limiting ve koruma

| Katman | Limit | Uygulama |
|---|---|---|
| IP bazlı (anonim) | 60 rpm | Ingress |
| API anahtarı | plana göre (600-10.000 rpm) | Redis token bucket |
| Organizasyon toplam | plana göre | Redis |
| Sorgu maliyeti | max 5 sn, max 10.000 satır, max derinlik 5 | Gateway |
| Eşzamanlı bağlantı | plana göre | PgBouncer |
| Yavaş sorgu | 5 sn'de `statement_timeout` | Postgres |
| Payload boyutu | 10 MB | Ingress |

Faz 1'deki kullanıcı-partitionlı rate limiter mantığı **doğruydu** ve korunur; Redis'e taşınır (çok instance için).

---

## 6. Metadata cache ve sıcak yeniden yükleme

```
Gateway başlar
 → metadata çek (control plane) → bellek + Redis
 → NATS `schema.version.changed` aboneliği
 → olay gelince: ilgili proje metadata'sını tazele (restart yok)
 → in-flight istekler eski sürümle tamamlanır (tutarlılık)
```
Cache anahtarı: `n:{env}:meta:{projectId}:{env}` · TTL 60 sn · negatif cache 5 sn

---

## 7. İstemci SDK üretimi

Şemadan otomatik, sürümlü:

| Dil | Paket | İçerik |
|---|---|---|
| TypeScript | `@namines/client` + proje tipleri | Tam tipli CRUD, filtre builder, React hook'ları |
| C# | `Namines.Client` | `IProjectClient`, tipli entity'ler |
| Python | `namines` | Pydantic modelleri |
| Go | `namines-go` | struct + client (P3) |

```ts
import { createClient } from '@namines/client';
import type { Database } from './namines.types';   // otomatik üretilir

const nam = createClient<Database>({ url: '...', key: process.env.NAMINES_KEY });

const { data } = await nam
  .from('orders')
  .select('id, total, user(display_name)')
  .eq('status', 'paid')
  .gte('placed_at', '2026-01-01')
  .order('placed_at', { ascending: false })
  .limit(50);
// data: { id: bigint; total: string; user: { display_name: string } }[]
```

Tipler CI'da `npx namines codegen` ile yenilenir; şema kırıcı değiştiyse **derleme hatası verir** — bu, "şema değişti, uygulamam sessizce bozuldu" problemini çözer ve güçlü bir satış argümanıdır.

---

## 8. Sürümleme ve kırıcı değişiklikler

| Değişiklik | API etkisi | Davranış |
|---|---|---|
| Kolon eklendi (nullable) | Uyumlu | Anında yansır |
| Kolon eklendi (not null, default'lu) | Uyumlu | Anında |
| Kolon silindi | **Kırıcı** | 30 gün `deprecated` olarak yanıtta kalır (null), sonra kaldırılır |
| Kolon yeniden adlandırıldı | **Kırıcı** | Eski ad 30 gün alias olarak çalışır |
| Tip daraltıldı | **Kırıcı** | Onay + istemci uyarısı |
| Tablo silindi | **Kırıcı** | 410 Gone + migration notu |

`Namines-Api-Version` header'ı ile istemci belirli bir şema sürümüne pinlenebilir (P2).

---

## 9. Realtime (P2)

```
WS /v1/{slug}/realtime
→ subscribe: { table: "orders", event: "*", filter: "status=eq.pending" }
← { event: "INSERT", table: "orders", record: {...}, at: "..." }
```
Kaynak: PostgreSQL logical replication (`wal2json`) → NATS → WebSocket fan-out. RLS aynı şekilde uygulanır (kullanıcının göremeyeceği satır gönderilmez).

---

## 10. Performans notları

- Tüm sorgular **prepared statement**, plan cache'li
- `expand` tek sorguda `LATERAL JOIN` + `json_agg` ile → N+1 yok
- Sayım varsayılanı `estimated` (`pg_class.reltuples`) — büyük tabloda `COUNT(*)` felaket
- Sonuç streaming (`IAsyncEnumerable` + `System.Text.Json` writer) — büyük export'ta bellek sabit
- Native AOT derlemesi → soğuk başlangıç < 50 ms
