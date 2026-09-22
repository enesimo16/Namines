# 30 — Sunucu Taraflı Branching

> Faz 1'in "cihaz başına yerel" branch modelinden, [29-DATABASE-CHANGE-REVIEW.md](29-DATABASE-CHANGE-REVIEW.md)'in
> ihtiyaç duyduğu gerçek, çok kullanıcılı, sunucu-otoriteli modele geçiş.

---

## 1. Bugünkü sorun (somut)

Mevcut branch özelliği istemci tarafında tutuluyor — README'nin kendi itirafıyla
"branch'ler yerelde tutulur, cihaz başına." Bunun anlamı:

- İki kullanıcı aynı branch'i **göremez** (her biri kendi cihazında farklı bir kopya)
- `Database Change Review` ekranı (29) **sunucuda kalıcı bir branch kaydı olmadan çalışamaz** — kimin neyi onayladığını, hangi branch'in hangi durumda olduğunu bilecek tek yer sunucu olmalı
- Branch DB'si (ephemeral veya kalıcı) bir client-side kavrama bağlanamaz

Bu yüzden 30, 29'un **ön koşuludur** — Change Review UI'ı bundan önce anlamlı çalışamaz.

---

## 2. Hedef model

[18-CONTROL-PLANE-DDL.md](18-CONTROL-PLANE-DDL.md)'de `branches` ve `schema_versions`
tabloları **zaten tanımlı** (Faz 2 planlamasında yazılmıştı). G7 (SQLite→PostgreSQL)
tamamlanınca bu tablolar gerçek control DB'de yaşayabilir — G7'nin öncelik artışının
sebebi bu.

```sql
-- 18'den, hatırlatma:
CREATE TABLE branches (
    id           char(26)     PRIMARY KEY,
    project_id   char(26)     NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    name         varchar(120) NOT NULL,
    parent_id    char(26)     REFERENCES branches(id) ON DELETE SET NULL,
    forked_from_version integer,
    is_default   boolean      NOT NULL DEFAULT false,
    created_by   char(26)     NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    created_at   timestamptz  NOT NULL DEFAULT now(),
    closed_at    timestamptz
);
```

**Faz 0/mevcut mimariyle fark:** Bu tablo, bugünkü `CloudProjects` modelinin **yanına**
eklenir, onu değiştirmez. Mevcut proje kaydetme akışı bozulmaz — branch, projenin
altında yeni bir kavram olarak eklenir.

---

## 3. Aşamalı geçiş (rewrite değil)

Kullanıcının 15. maddesindeki "sıfırdan rewrite etme" prensibine uygun, üç adım:

### Adım 1 — Sunucuda branch kaydı (şema DB'de tutulmaya devam eder)
`branches` tablosu eklenir ama şemanın kendisi hâlâ `CloudProjects.SchemaJson`'da
(veya `schema_versions.nsl_inline`, NSL gelene kadar mevcut JSON formatında) tutulur.
Bu adımda **canlı DB branch'i yok**, sadece "şemanın adı X olan bir kopyası var"
kavramı sunucuya taşınıyor. Küçük, düşük riskli, mevcut CRDT/realtime katmanına
dokunmuyor.

### Adım 2 — CRDT dokümanı branch'e bağlanır
[10-REALTIME-COLLAB.md §5](10-REALTIME-COLLAB.md)'de zaten tarif edilen model:
`crdt_documents` tablosu (branch_id → Yjs state). Bu adım gerçek zamanlı işbirliğinin
branch bazlı çalışmasını sağlar — CanvasHub'ın bugünkü `roomId` kavramı `branch_id`'ye
eşlenir (G6'da hardened edilen `IPresenceStore` doğrudan yeniden kullanılır, değişmez).

### Adım 3 — Branch DB (ephemeral, Data Plane'den önce minimal versiyon)
Tam [06-DATA-PLANE.md](06-DATA-PLANE.md) planlanmadan, **G5'in Testcontainers
altyapısının runtime'a taşınmış hali** yeterli: "Run Tests" (29 §4) her branch için
tek seferlik bir Testcontainers instance'ı başlatabilir, kalıcı bir provisioning
sistemi (Neon vb.) gerekmeden. Bu, Data Plane'in tam inşasını beklemeden Change
Review'ı çalışır hale getirir — **ucuz, hızlı bir MVP köprüsü.**

---

## 4. Neyin ERTELENDİĞİ (bilinçli)

| Özellik | Ne zaman |
|---|---|
| Neon/PlanetScale gerçek copy-on-write branch DB | [06-DATA-PLANE.md](06-DATA-PLANE.md), Data Plane fazı |
| PII maskeleme branch'lerde | Data Plane fazı, kurumsal katman |
| Branch'ler arası 3-way merge (mevcut `mergeSchemas` client-side kodu) | Sunucuya taşınır ama ADIM 1-2'den sonra, ayrı iş |
| Git repo senkronu (`.nsl` dosyası) | NSL'den sonra ([04](04-NSL-SCHEMA-IR.md)) |

**Neden Adım 3'te tam Data Plane beklenmiyor:** Kullanıcının "önce küçük bir alanı
çok iyi çöz" prensibi — Testcontainers ile geçici bir DB açmak, gerçek bir provisioning
platformu inşa etmekten çok daha ucuz ve Change Review'ın değerini kanıtlamak için
yeterli. Gerçek müşteri talebi doğrulanınca Data Plane'e geçilir.

---

## 5. Güvenlik notu (G1/G6 ile tutarlılık)

Branch DB'leri (Testcontainers tabanlı olsa bile) **G1'de kaldırdığımız `docker.sock`
mount modeline geri dönmemeli.** Aynı 06-DATA-PLANE.md §2'deki kural geçerli: worker
süreci Docker API'sine doğrudan container içinden değil, izole bir provisioning
adımından erişmeli. MVP köprüsünde (Adım 3) bu, worker'ın **kendi** host'unda
(container içinde değil) Testcontainers çalıştırması ile basitçe sağlanır — G5'te
zaten bu şekilde çalıştı.
