# 20 — Teknik borç kaydı

**Ölçüm notu:** Bu depoda `TODO`, `FIXME`, `HACK` **hiç yok** (62k satır C# +
38k satır TS tarandı, 0 eşleşme). Aşağıdaki borç, işaretlenmemiş ama gerçek
olan borçtur — kod okumasıyla çıkarıldı.

---

## TD-001 — `frontend/` için sıfır otomatik test

**Neden var:** `frontend` önce yazıldı; test disiplini sonradan (`services/desk`
ile birlikte) geldi ve geriye dönük uygulanmadı.

**Etki:** Kod tabanının %31'i (31.658 satır) doğrulanmadan üretime gidiyor.
Regresyonlar yalnızca kullanıcı tarafından fark ediliyor.

**Düzeltme eforu:** L (ilk anlamlı kapsam için ~1 hafta)
**Öncelik:** P1
**Başlangıç noktası:** `useSchemaStore` (804), `useProjectHistoryStore` (711),
`lib/templates.ts` (1.942) — üçü de saf TypeScript, React gerekmeden test edilir.

---

## TD-002 — `GatewayController` 1.447 satır, yetki kontrolü elle tekrarlanıyor

**Neden var:** Gateway aşamalı olarak büyüdü (önce salt-okunur liste, sonra tam
yazma yüzeyi, sonra rpc/query/query-nl). Her ekleme aynı dosyaya yapıldı.

**Etki:** Yeni bir uç eklerken yetki kontrolünü unutmak mümkün ve derleyici
uyarmaz. Sınıf `[AllowAnonymous]` olduğu için varsayılan **açık** tarafa düşüyor.

**Düzeltme eforu:** L
**Öncelik:** P1
**Yaklaşım:** Anahtar çözme + izin kontrolünü bir filtreye taşı → varsayılan
kapalı olur.

---

## TD-003 — `ScaffolderService` 1.760 satır

**Neden var:** Üreteç kodu doğal olarak uzun; bölme işi ertelenmiş.
**Etki:** Bakım zorluğu. Davranışsal risk yok.
**Efor:** M · **Öncelik:** P2
**Yaklaşım:** `Generators/Eject/` altındaki mevcut bölme desenini uygula.

---

## TD-004 — `AIPreferencesModal.tsx` 1.521 satır, 5 sorumluluk

**Neden var:** Ayarlar ekranı zamanla her yeni tercihin eklendiği yer oldu.
**Etki:** Değişiklik yapmak riskli; 11 `localStorage` anahtarı string olarak dağınık.
**Efor:** M · **Öncelik:** P2

---

## TD-005 — Docker.DotNet sürüm çatışması

**Neden var:** `Namines.Vault` 3.125.15 kullanıyor; `Testcontainers` 4.13 →
Docker.DotNet 4.x getiriyor. Test bin klasöründe 4.x kazanıyor.

**Etki:** Bu oturumda **gerçekten patladı** (`TypeLoadException`). Geçici olarak
Docker istemcisi `Lazy<T>` yapılarak aşıldı; çatışma duruyor.

**Efor:** M · **Öncelik:** P2
**Not:** Vault'u 4.x'e taşımak, canlı doğrulanmış yedek/geri yükleme akışlarının
yeniden kanıtlanmasını gerektirir. Bu, borcun gerçek maliyeti.

---

## TD-006 — Hata sınıflandırması yeniden kullanılamaz

**Neden var:** `ClassifyConnectionFailure` `GatewayKeyController` içinde
`private` yazıldı; ihtiyaç ikinci kez doğduğunda taşınmadı.

**Etki:** `DatabaseExecutorController` ham sürücü mesajını sızdırıyor (SEC-002).
Aynı sorunun iki farklı cevabı var.

**Efor:** S · **Öncelik:** P1

---

## TD-007 — Migration açılışta uygulanıyor

**Neden var:** Geliştirme kolaylığı. Üretim ayrımı hiç yapılmadı.
**Etki:** Deploy anında geri alınamaz şema değişikliği, onay adımı olmadan.
Çoklu instance'ta yarış riski.
**Efor:** M · **Öncelik:** P2

---

## TD-008 — İki frontend, iki standart

**Neden var:** `services/desk` sonradan ve daha olgun bir yaklaşımla yazıldı.

**Etki:** Aynı depoda iki farklı kalite standardı:

| | `frontend/` | `services/desk/` |
|---|---|---|
| Test | 0 | 99 |
| `tsc --noEmit` script'i | ❌ | ✅ |
| Boş durumlar | ✅ | ❌ |
| Ham `fetch` kullanımı | Var | Yok |

İlginç olan: ikisi de diğerinin iyi olduğu yerde kötü. Karşılıklı öğrenme
en ucuz kazanç.

**Efor:** M · **Öncelik:** P2

---

## TD-009 — Optimistic concurrency yok

**Neden var:** Tek kullanıcılı varsayımla başlandı; ekip özellikleri sonradan eklendi.
**Etki:** İki kişi aynı projeyi düzenlerse biri sessizce kaybeder.
**Efor:** M · **Öncelik:** P2

---

## TD-010 — Sahte "Personal Access Token" ekranı

**Neden var:** Muhtemelen erken bir UI taslağı; gerçek Gateway anahtar sistemi
yazıldıktan sonra kaldırılmadı.
**Etki:** Ölü kod + yanlış güvenlik sözü (SEC-003).
**Efor:** S · **Öncelik:** **P0** — bu, borç değil **hata**; borç listesine
tamlık için konuldu.

---

## TD-011 — `pageSize` üst sınırı ve istek boyutu limitleri doğrulanmadı

**Neden var:** Denetim kapsamı yetmedi.
**Etki:** Bilinmiyor — bu belirsizliğin kendisi borç.
**Efor:** XS (teyit) · **Öncelik:** P1

---

## Borç özeti

| Öncelik | Adet | Toplam efor |
|---|---|---|
| P0 | 1 | S |
| P1 | 4 | L + S + S + XS |
| P2 | 6 | ~4 × M |

**Genel değerlendirme:** Bu proje için teknik borç **düşük**. 100k satırlık bir
kod tabanında 11 kalem, hiçbiri "yeniden yaz" seviyesinde değil. Sıfır TODO ve
sıfır derleme uyarısı, borcun aktif olarak ödendiğini gösteriyor.

Asıl borç **kodda değil, kapsamda**: frontend testi ve üretim dağıtım tanımı.
