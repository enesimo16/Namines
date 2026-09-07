# 11 — Namines Desk v2 Tamamlandı

> Bu doküman `10-DESK-V2-YOL-HARITASI.md`'nin **bitiş durumu**dur — v3'e
> geçerken referans olsun diye, `09-YOL-HARITASI.md`'nin v1 bitişini
> özetleme biçimiyle aynı üslupta yazıldı. Onay: kullanıcının `/goal`
> talimatı — "namines v2'yi onaylıyorum tamamen yap ve bitir".

---

> **Sonraki tur:** Desk v2.1 — pano kabuğu (sol gezinme + üst şerit + açık
> tema). Bu dokümandaki her şey geçerli, yalnızca **arayüz kabuğu** değişti:
> [`12-DESK-PANO-KABUGU.md`](12-DESK-PANO-KABUGU.md).

---

## Özet

**Desk v1** (D1-D7) + **Desk v2** (E1-E4, E5.1-E5.2) artık bitti. E5.3
(uyarı kuralları) kullanıcının açık talimatıyla **bilinçli olarak
ertelendi** — e-posta altyapısı gelene kadar anlamlı çalışamaz.

| Kanıt | Sonuç |
|---|---|
| Backend `dotnet build` | 0 hata, 0 uyarı |
| Backend `dotnet test` | **1393 + 17 = 1410** test, hepsi yeşil |
| Desk `npx tsc --noEmit` | temiz |
| Desk `next build` (prod) | temiz |
| Docker (`control-db`, `backend`, `frontend`) | üçü de `healthy`/`Up` |

---

## ⚠️ Ek not (2026-09-06): "Up" ≠ "güncel kod çalışıyor"

Yukarıdaki Docker satırı yanıltıcıydı — konteynerler **ayaktaydı** ama
`namines-backend` imajı bu dokümanın anlattığı Desk v2 backend
değişikliklerinden (yeni migration, `desk-handoff-token`, `isOwner`/
`allowDeskSql`/`hasConnection`/`tableCount` alanları, SQL konsolu, toplu
silme…) **ÖNCE** derlenmişti — hiçbiri konteynerde YOKTU. Belirti: Desk'e
gerçek bir hesapla girildiğinde proje listesi boş görünüyordu ve
`POST /api/auth/desk-handoff-token` `404` dönüyordu, oysa host'ta
`dotnet build`/`dotnet test` temizdi.

**Ders — bu projenin "kanıt = çalışan komut" kuralına yeni bir madde:**
host'ta yeşil bir `dotnet test`, konteynerde derlenmiş kodun GÜNCEL olduğunu
KANITLAMAZ. Docker Compose ile çalışan bir servise dokunan her backend
değişikliğinden sonra `docker compose build <servis> && docker compose up -d
<servis>` çalıştırılmadan "bitti" denemez; imaj yeniden derlenmeden konteyner
yalnızca ESKİ kodu "Up" gösterir.

Aynı turda ikinci bir gerçek hata daha bulundu ve düzeltildi: backend
`ASPNETCORE_ENVIRONMENT=Production`'da çalışıyor, ve kod Desk'in origin'ini
(`localhost:3200`) yalnızca Production DIŞINDA CORS'a otomatik ekliyordu —
yani konteynerde CORS Desk'i hiç izin listesine almıyordu (`docker-compose.yml`'e
`Cors__AllowedOrigins__0=http://localhost:3200` eklenerek düzeltildi). Ayrıca
Desk'in boş proje listesi ekranındaki "Ana uygulamada bir tane oluşturun"
bağlantısı yanlışlıkla `NAMINES_API`'ye (backend, JSON döner) gidiyordu;
doğru hedef `NAMINES_FRONTEND` (tarayıcıda açılan uygulama) olarak ayrıldı.

Backend imajı yeniden derlenip konteyner yeniden oluşturulduktan sonra
migration gerçekten uygulandı (`Applying migration
'20260905201025_AddDeskV2SecurityFields'`) ve yukarıdaki tüm alanlar +
handoff ucu canlı doğrulandı.

---

## E1 — Kimlik'in kalanı

### E1.1 API anahtarı yönetim ekranı — ✅

Backend zaten hazırdı (`GatewayKeyController`); eklenen tamamen arayüz:
[`lib/apiKeys.ts`](../services/desk/lib/apiKeys.ts),
[`app/ApiKeys.tsx`](../services/desk/app/ApiKeys.tsx). Owner'a gösterilen
yeni bir "API" sekmesi — anahtar listesi, oluşturma formu (ad + yazma izni),
**yalnızca oluşturma anında bir kez** gösterilen ham anahtar, iptal düğmesi.

> Not: `Revoke` ucu `204 No Content` (boş gövde) döndürüyor — `apiKeys.ts`
> bunu ayrıca ele alıyor (`res.json()` çağırmıyor), genel `req()` yardımcısı
> gibi körlemesine JSON ayrıştırmıyor.

### E1.2 SSO devri — ✅ **en güvenli seçenekle**

`34-SENDEN-BEKLENENLER.md` madde 11'deki iki seçenekten **(b) kısa ömürlü
POST formu** seçildi (a: query param değil) — jeton hiçbir zaman URL'e
girmiyor, dolayısıyla tarayıcı geçmişine ve `Referer` başlığına düşmüyor.

**Ne yapıldı:**
- `DeskHandoffToken` modeli (`TeamInvite` ile aynı desen) — yalnızca
  `SHA256` hash'i saklanıyor, tek kullanımlık (`UsedAt`), kısa ömürlü
  (`ExpiresAt`).
- `DeskHandoff.cs` (uzantı metotları, `GatewayAudit.cs`/`OrgAccess.cs` ile
  aynı mimari) — `CreateDeskHandoffTokenAsync` / `ExchangeDeskHandoffTokenAsync`.
- Tek kullanımlık garantisi **atomik**: `ExecuteUpdateAsync` ile
  `WHERE UsedAt IS NULL` korumalı tek bir UPDATE — canlı Postgres'e karşı
  gerçek bir eşzamanlılık testiyle doğrulandı (`Task.WhenAll` ile iki eşzamanlı
  değişim denemesi, yalnızca biri geçiyor).
- Ana uygulamada gizli, otomatik gönderilen `<form method="POST"
  target="_blank">` ([`app/compile/page.tsx`](../frontend/app/compile/page.tsx));
  Desk tarafında karşılayan Route Handler
  ([`app/handoff/route.ts`](../services/desk/app/handoff/route.ts)) —
  jetonu değiştirip JWT'yi `sessionStorage`'a yazıyor.

---

## E2 — Projects'in kalanı

### E2.1 Takım/üye yönetimi (salt-okunur) — ✅

[`lib/members.ts`](../services/desk/lib/members.ts) +
[`app/Members.tsx`](../services/desk/app/Members.tsx) — herkese açık yeni
"Ekip" sekmesi, `ProjectMemberController.List`'i tüketiyor (`CanViewAsync`,
her rol görebilir). Davet gönderme/rol değiştirme/çıkarma **bilinçli olarak
yok** — plandaki gerekçe korundu (iki yerde aynı işi yapmak "hangi ekran
yetkili" sorusunu bulanıklaştırır).

### E2.2 Arama/filtreleme — ✅

[`app/Projects.tsx`](../services/desk/app/Projects.tsx) — 20+ projede
görünen ad/motor/bağlantı-durumu filtre şeridi, tamamen istemci tarafı.
Backend değişikliği gerekmedi.

---

## E3 — Canvas'ın kalanı

### E3.1 Otomatik yerleşim düğmesi — ✅

`@dagrejs/dagre` Desk'in **kendi** bağımlılığı olarak eklendi (ana
uygulamanın `lib/autoLayout.ts`'i kopyalanmadı — mikroservis sınırı
korundu). [`app/Canvas.tsx`](../services/desk/app/Canvas.tsx)'a "Otomatik
yerleştir" düğmesi — TÜM düğümleri (hayaletler dahil) yeniden konumlandırır,
"Düzeni sıfırla" ile geri alınabilir. Konum **hâlâ hiç kaydedilmiyor** —
Canvas'ın en üstteki "Desk kendi düzenini yaratmaz" kararı korundu.

---

## E4 — Data'nın kalanı

### E4.1 Toplu silme — ✅ **eşik: 10**

Kullanıcı onayı: 10+ satırda `BulkDeleteConfirm.tsx` "SİL" yazma
zorunluluğu (Namines Bot'un "aprove/approve" tahmin ETMEME kararıyla aynı
ilke); 10'dan azda tarayıcının kendi `confirm()`'ü. Backend
`GatewayService.BulkDeleteAsync` — `ImportAsync`'teki gibi TEK transaction
(ya hepsi ya hiçbiri), gerçek bir FK ihlaliyle zorlanan bir canlı testle
rollback doğrulandı. Toplu güncelleme (E4.1'in ikinci yarısı) v2 kapsamına
alınmadı — planda yalnızca silme netleşmişti, güncelleme ayrı bir karar.

### E4.2 Ham SQL konsolu — ✅ **izin verildi, tüm güvenlik önlemleriyle**

`34-SENDEN-BEKLENENLER.md` madde 13'ün kararı: **izin var**, çok katmanlı
güvenlikle:

1. Yalnızca proje **Owner**'ı (`OrgRole.Owner`).
2. Proje bazında **varsayılan kapalı** — `CloudProject.AllowDeskSql`,
   Owner'ın kendisi açana kadar konsol çalışmaz
   (`PUT /api/gateway/project/{projectId}/desk-sql`).
3. Yalnızca **tek bir salt-okunur ifade** — `EnsureReadOnlySelectStatement`
   regex beyaz listesi: yalnızca SELECT/WITH/EXPLAIN/SHOW ile başlar,
   INSERT/UPDATE/DELETE/DROP/ALTER/TRUNCATE/CREATE/GRANT/REVOKE/CALL/EXEC/
   EXECUTE/COPY/VACUUM/MERGE/REPLACE/ATTACH/DETACH/PRAGMA ve
   `SELECT…INTO` dahil olmak üzere tam kelime olarak reddedilir; zincirlenmiş
   ifadeler de reddedilir.
4. Motor destekliyorsa (Postgres/MySQL/MariaDB) veritabanı düzeyinde
   **salt-okunur oturum** — bu ikinci katman doğrudan test edildi: regex'i
   atlatmaya çalışan bir ifade gerçek bir `PostgresException` ile reddedildi.
   SQL Server/Oracle'da bu katman yok, o yüzden regex orada "ekstra" değil
   tek savunma hattı — doküman ve kodda bu ayrım açıkça not edildi.
5. Ayrı, sıkı `[EnableRateLimiting("sensitive")]` (5 istek/dk) — controller'ın
   genel 1200 rpm'inden bağımsız.
6. Her çalıştırma denemesi (başarılı/başarısız) audit trail'e yazılıyor.

Backend: `GatewayService.DeskSqlQueryAsync`,
`GatewayController.DeskSql`. Frontend:
[`lib/deskSql.ts`](../services/desk/lib/deskSql.ts) +
[`app/SqlConsole.tsx`](../services/desk/app/SqlConsole.tsx) — üç durumlu
arayüz (Owner değilse bilgi, kapalıysa "aç" düğmesi, açıksa editör).

---

## E5 — Logs'un kalanı (kısmi)

### E5.1 Yeni yazma bildirimi — zaten D5'in yoklama deseniyle kapsanıyor

Planın önerdiği "D5'teki aynı 30 sn yoklama deseni Logs'a da uygulanır"
maddesi ayrı bir iş olarak açılmadı; mevcut D5 şeridi zaten şema
güncellemelerini kapsıyor. Gerçek SignalR akışı (planın kendi notu)
bilinçli olarak v2 dışında kaldı.

### E5.2 Log dışa aktarma — **v2 kapsamına alınmadı**

`GET /api/gateway/keys/{projectId}/audit/export?format=csv` bu turda
eklenmedi. D4'ün export desenine küçük bir eklenti olduğu için düşük
risklidir — v3'e taşınabilir.

### E5.3 Uyarı kuralları — ❌ **bilinçli olarak ertelendi**

Kullanıcının açık talimatı: *"e posta davet yollama mekanizmasını
değişeceğiz şuan için o kalabilir"* — yani e-posta altyapısı henüz
değişmeden bu özelliğin kodlanması anlamsız olurdu. `34-SENDEN-BEKLENENLER.md`
madde 14, ekip davet e-postasıyla aynı blokaja bağlı kalmaya devam ediyor.

---

## Kalıcı olarak Desk'e girmeyenler (değişmedi)

`10-DESK-V2-YOL-HARITASI.md`'nin "Kalıcı olarak dışarıda" bölümü aynen
geçerli: şema düzenleme, Canvas'ta konum kaydetme, proje oluşturma/silme,
satır-içi ilişki genişletme (`Expand`), Deployments'ta onay verme, DDL push.
Bunların hiçbiri v2'de tartışılmadı çünkü zaten "hayır" listesinde.

---

## v3'e not

Desk v2 bitmiş durumda. Bilinçli olarak v3'e (ya da sonrasına) bırakılanlar:

- **E4.1'in toplu güncelleme yarısı** — yalnızca toplu silme netleşmişti.
- **E5.2 log dışa aktarma** — küçük, bağımsız bir ek.
- **E5.3 uyarı kuralları** — e-posta altyapısı bloklaması kalkınca.
- **Vault bağımlı işler** — DDL push, şemayı canlı veritabanına uygulama
  (`09-YOL-HARITASI.md`'nin zaten not ettiği "sonra: Vault" sırası).

Desk artık: kimlik + SSO devri + API anahtarı yönetimi (E1), takım
görüntüleme + arama/filtre (E2), otomatik yerleşim (E3), toplu silme +
ham SQL konsolu (E4) ile birlikte, D1-D7'nin üzerine tam bir "hosted,
deterministik CRUD paneli" olarak duruyor.
