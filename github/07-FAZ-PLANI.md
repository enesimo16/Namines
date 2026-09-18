# 07 — Faz Planı

> Her faz **tek başına teslim edilebilir** ve kendi başına bir değer üretir.
> Sıra, bağımlılığa göre değil **beklemeye** göre dizildi: senden hesap işi
> bekleyen fazlar, beklemeyenlerin arkasına kondu.

| Faz | Ne | Seni bekler mi | Dayandığı iş |
|:--:|---|:--:|---|
| **F0** | Kayıt defteri + `+` menüsü | ❌ | — |
| **F1** | Public repo → şema / drift | ❌ | F0 |
| **F2** | GitHub App, private repo, bot canlı | ✅ App | F1 |
| **F3** | GitHub branch ↔ Namines branch + PR'da preview DB | ❌ | F2 |
| **F4** | Sunucu-taraflı 3-yollu merge + merge kuyruğu | ❌ | F3 |
| **F5** | Yazma yolu: PR → izin butonu → push | ❌ | F2 (izin kapsamı) |
| **F6** | Diğer kaynakların eklentileşmesi + CLI/Action yayını | ✅ npm | F0, F5 |

---

## F0 — Kayıt defteri ve `+` menüsü

**Ne:** `ISchemaSource` sözleşmesi ([06](06-EKLENTI-MIMARISI.md) §3),
`ProjectRepository` modeli ([00](00-GENEL-BAKIS.md) §2), ve prompt kutusundaki
dağınık ikonların tek bir `+ Kaynak ekle` menüsüne toplanması. Var olan
kaynaklar **taşınmaz**, yalnızca menüden çağrılır — davranış değişmez.

**Neden ilk:** GitHub'ın takılacağı yuva bu. Yuva olmadan GitHub yine bir
düğme olur ve "eklentileştirme" hiç gelmez.

**Kanıt:** menüden çağrılan her mevcut kaynak eskisiyle aynı sonucu veriyor
(OpenAPI URL, kod dosyası, resim, DB bağlantısı — dördü de elle denendi).

**Risk:** UI regresyonu. Mevcut akışların hiçbiri kaldırılmıyor, yalnızca
menüye taşınıyor; `FRONTEND.md` kuralları ve `ui-ux-pro-max` zorunlu.

---

## F1 — Public repo → şema / drift

**Ne:** [01](01-DEPO-TARAMA.md)'in tamamı. `IGithubClient`'a tree + toplu
dosya okuma + branch listesi; `RepositoryScanner`; `POST /api/github/scan`;
`+` menüsünde "GitHub deposu" girişi.

**Değer:** *"linki yapıştır, şemanı gör"* — App beklemeden çalışan, tek başına
gösterilebilir bir özellik. Aynı zamanda F2'nin bütün ayrıştırma yolunu
önceden doğrular.

**Kanıt:** [01](01-DEPO-TARAMA.md)'in doğrulama listesi (5 gerçek depo).

**Risk:** rate limit ve dev monorepo'lar. İkisi de "atlandı" raporuyla
dürüstçe karşılanıyor.

---

## F2 — GitHub App, private repo, bot canlı

**Ne:** App'in kurulması (34 §8 — **senden bekleniyor**), `installation_id`
yakalama, `ProjectRepository` kaydı, private depo tarama, bot'un gerçek PR'da
yorum + `namines/schema-review` check yazması.

**Kapsam:** `contents: read`, `pull_requests: write`, `checks: write`.
`contents: write` **burada istenmiyor** (F5'te).

**Değer:** ürünün en kısa değer cümlesi burada tamamlanıyor — required check
açılınca yıkıcı şema değişikliği merge edilemiyor.

**Kanıt:** gerçek bir private depoda gerçek bir PR; yorum düştü, check göründü,
yıkıcı değişiklikte `failure`.

**Risk:** bu faz **senin bir hesap işine bağlı.** F1 ve F3'ün kod tarafı bu
arada ilerleyebilir; bot'un mantığı zaten kimlikten ayrılmış durumda
(`IGithubClient` arayüzü tam bunun için var).

---

## F3 — Branch eşlemesi ve PR'da preview DB

**Ne:** GitHub branch/PR ↔ Namines branch eşlemesi; webhook'un var olan
`BranchController` uçlarını tetiklemesi:

```
pull_request.opened      → POST /api/branch/{id}/database  (+ seed)
pull_request.synchronize → destroy + provision (yeniden kur)
pull_request.closed      → DELETE /api/branch/{id}/database
```

PR yorumuna preview bağlantısı (Desk linki) ve risk tablosu eklenir.

**Neden küçük bir faz:** provisioner, TTL süpürücü, kota, seed **zaten yazılmış**
([02 §4](02-BOT-VE-PREVIEW-DB.md)). Yazılacak olan eşleme tablosu ve tetikleyici.

**Kanıt:** PR açıldı → Desk'ten preview DB'ye gerçekten bağlanıldı → PR
kapandı → container `docker ps -a` ile **bağımsız** doğrulanarak silinmiş.

**Risk:** Docker Desktop kırılganlığı (AGENTS.md). Provision **sıraya alınır**,
paralelleştirilmez.

---

## F4 — Sunucu-taraflı merge motoru ve merge kuyruğu

**Ne:** [03](03-COKLU-GELISTIRICI-MERGE.md)'ün tamamı — `merge-base ↔ A ↔ B`
üçlü IR karşılaştırması, `auto | needs-decision | incompatible` sınıflandırması,
merge kuyruğu, merge anında versiyon atama, canvas'ta "PR #12'de değiştiriliyor"
rozeti.

**Önemli:** çakışma **arayüzü zaten var** (`ConflictResolverModal` +
`useBranchStore`). Bu faz onu **beslemek**, yeniden yazmak değil. Mevcut
`MergeConflictItem` tipi sözleşme olarak alınır; sunucu onu üretir.

**Değer:** "3-4 kişi aynı anda çalışıyor, DB karışıyor" probleminin asıl cevabı.

**Kanıt:** üç senaryonun üçü de elle kurulup denenir — aynı kolon farklı tiple,
yeniden adlandırma + FK, iki PR'da aynı versiyon numarası.

**Risk:** en zor faz. Integration DB'nin her denemeden önce bilinen iyi duruma
dönmesi şart, yoksa sonraki denemeler yanlış tabana karşı çalışır.

---

## F5 — Yazma yolu

**Ne:** [04](04-YAZMA-YOLU-VE-IZIN.md)'ün tamamı. `WriteMode` (`none →
pull-request → push`), izin butonu ve ikinci onay ekranı, blob/tree/commit/ref
+ PR uçları, denetim kaydı, yıkıcı değişikliğin izinden bağımsız PR'a düşmesi,
`/namines` komutlarının gerçekten çalışması, tip senkronu.

**Kapsam artışı:** `contents: write` (+ `pull_requests: write` zaten var).
İzin ekranında **neden istendiği** yazılı.

**Kanıt:** izin kapalıyken yazma denenmiyor; `pull-request` modunda PR açılıyor;
`push` modunda güvenli değişiklik gidiyor ama yıkıcı olan PR'a düşüyor; izin
geri alınınca bekleyen iş iptal oluyor.

**Risk:** izin tırmanması kullanıcıyı ürkütebilir. Karşılığı: izin **özellik
başına** isteniyor, hepsi baştan değil.

---

## F6 — Eklentileşmenin tamamlanması ve yayın

**Ne:** OpenAPI/GraphQL, kod dosyaları, DB bağlantısı, vision ve JSON şekli
kaynaklarının `ISchemaSource` sözleşmesine taşınması; Ayarlar → **Eklentiler**
sayfası (kurulu kaynaklar, izinler, son senkron); CLI'ın npm'e ve
`namines/setup-action`'ın yayınlanması ([05](05-CI-CD-VE-DIGER.md)).

**Neden en sonda:** taşıma işi, sözleşmenin **gerçek bir eklentiyle
(GitHub) sınanmış** olmasını gerektiriyor. Önce taşırsak, sözleşmeyi tek bir
kaynağın ihtiyaçlarına göre tasarlamış oluruz ve GitHub geldiğinde hepsini
yeniden yazarız.

**Bekleme:** npm hesabı + yayın (34 §2).

---

## Özet bağımlılık zinciri

```
F0 ──► F1 ──► F2 ──► F3 ──► F4
               └───► F5 ──► F6
```

F2 senden App'i, F6 npm'i bekliyor. Diğer dört faz beklemiyor.
