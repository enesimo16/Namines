# 14 — Kullanıcı senaryoları

> ⚠️ **Sınır:** Bu senaryolar **koddan** izlendi, tarayıcıda uygulanmadı. Her
> senaryonun sonunda "elle doğrulanmalı" notu var. Bu, gerçek bir kullanılabilirlik
> testinin yerini tutmaz — onun için hazırlanmış bir kontrol listesidir.

---

## Senaryo 1 — Yeni kullanıcı

| # | Adım | Kod yolu | Değerlendirme |
|---|---|---|---|
| 1 | Siteye gelir | `frontend/app/page.tsx` | — |
| 2 | Kayıt | `POST /api/auth/register` | ⚠️ Parola politikası zayıf (SEC-007) |
| 3 | Giriş | `POST /api/auth/login` | ⚠️ Kaba kuvvet koruması yok (AUTH-002) |
| 4 | Dashboard | `/canvas` ya da `/new` | 🟡 Hangisi? Yönlendirme doğrulanmadı |
| 5 | İlk veritabanını ekler | `PUT /api/gateway/keys/project/{id}/connection` | ✅ Bağlantı **kaydedilmeden önce** doğrulanıyor |
| 6 | Veritabanını açar | `POST /api/dbintrospect` | ✅ SSRF korumalı |
| 7 | Tabloları inceler | Canvas | ✅ |
| 8 | Sorgu çalıştırır | Desk `SqlConsole.tsx` | 🟡 Ayrı uygulamada — geçiş nasıl? (UX-004) |

**En güçlü nokta (5):** bağlantı dizesi kaydedilmeden ÖNCE gerçekten bağlanılıp
şema okunabildiği doğrulanıyor. Yani "bağlı" rozeti yalan söylemiyor. Bu, çoğu
rakipte olmayan bir titizlik.

**En büyük sürtünme (4→8):** kullanıcı canvas'ta mı, Desk'te mi olduğunu ve
neden geçtiğini anlamak zorunda kalıyor.

**Elle doğrulanmalı:** kayıt sonrası nereye düşüyor? İlk ekranda ne yapması
gerektiği söyleniyor mu?

---

## Senaryo 2 — Veritabanı geliştiricisi

| # | Adım | Değerlendirme |
|---|---|---|
| 1 | DB bağlar | ✅ |
| 2 | Şema inceler | ✅ Introspection 6 motoru destekliyor |
| 3 | Tablo seçer, kolonları görür | ✅ |
| 4 | Sorgu çalıştırır | ✅ Desk SQL konsolu |
| 5 | Sonucu inceler | ✅ Sayfalama var |
| 6 | **Sorguyu kaydeder** | ❌ **Özellik yok** |
| 7 | **Sorgu geçmişi** | ❌ **Özellik yok** |

**Bulgu:** Kaydedilmiş sorgu ve sorgu geçmişi yok. Bu, bir SQL konsolu sunan
her üründe **beklenen** iki özellik. Bkz. `18-feature-recommendations.md`.

**Not (MySQL/MSSQL/Oracle):** `DURUM.md` MySQL introspection'ında bulunan iki
gerçek hatayı kaydediyor (FK ilişkileri hiç okunmuyordu; `AUTO_INCREMENT`
yanlış kolondan okunuyordu). MySQL düzeltilip canlı doğrulandı; **MSSQL ve
Oracle ilişki sorguları yazıldı ama doğrulanmadı**. Yani (2) adımı bu iki
motorda güvenilir değil.

---

## Senaryo 3 — Yönetici

| # | Adım | Kod yolu | Değerlendirme |
|---|---|---|---|
| 1 | Kullanıcı davet eder | `TeamController` + `TeamInvite` | ✅ Jeton hash'i saklanıyor |
| 2 | Rol verir | `OrgRole` | ✅ 5 rol tanımlı |
| 3 | İzin verir | `GatewayTablePermission` | ✅ **Tablo başına** okuma/yazma |
| 4 | DB erişimini sınırlar | Gateway API anahtarı | ✅ |
| 5 | Denetim kaydını inceler | `GatewayAuditEntry`, `ChangeRequestAuditLog` | 🟡 **Kısmî** |

**Boşluk (5):** Denetim kaydı Gateway ve ChangeRequest için var; **executor ve
introspection için yok** (SEC-001). Yönetici "kim hangi SQL'i çalıştırdı"
sorusuna cevap alamıyor.

---

## Senaryo 4 — Tehlikeli işlem (`DROP TABLE`)

Bu senaryo, ürünün iddiasını en doğrudan sınayan senaryo. **İki farklı yol var
ve farklı davranıyorlar:**

| Yol | Uyarı | Onay | İzin kontrolü | Denetim kaydı |
|---|---|---|---|---|
| **Change Request akışı** | ✅ Risk sınıflandırması | ✅ Onay politikası, oy | ✅ Rol bazlı | ✅ `ChangeRequestAuditLog` |
| **Vault geri yükleme** | ✅ | ✅ **DB adını yazdırıyor** | ✅ Yalnızca Owner | ✅ `VaultRestores` |
| **`/api/executor/execute`** | ❌ | ❌ | ❌ | ❌ |

**Sonuç:** Ürünün merkezî vaadi ilk iki satırda **mükemmele yakın** uygulanmış;
üçüncü satır hepsini baypas ediyor. Bu, denetimin en önemli tek bulgusu
(SEC-001) ve tam olarak bu senaryoda görünür hâle geliyor.

---

## Senaryo 5 — Arıza (bağlantı gidiyor)

| Soru | Cevap |
|---|---|
| Kullanıcı ne olduğunu anlıyor mu? | 🟡 Hata mesajı geliyor ama sınıflandırma yalnızca Gateway yolunda var |
| Yeniden deneyebiliyor mu? | ✅ Uygulama çökmüyor |
| Sistem çöküyor mu? | ❌ Hayır — 10 sn bağlantı, 15 sn sorgu zaman aşımı var |

**İyi örnek:** `GatewayKeyController.ClassifyConnectionFailure` hatayı güvenli
kategorilere ayırıyor — kullanıcı "bir hata oluştu" değil, nedenini görüyor.
**Kötü örnek:** executor ham sürücü mesajını gösteriyor (SEC-002) — hem güvenlik
sorunu hem de kullanıcı için okunmaz.

---

## Senaryo 6 — Büyük veri kümesi

| Kontrol | Durum |
|---|---|
| Sayfalama | ✅ Motor başına doğru SQL |
| Kararlı sayfalama (ORDER BY) | ✅ Kolon verilirse; verilmezse **uyarı UI'a bırakılmış** (DB-005) |
| **`pageSize` üst sınırı** | ⚠️ **Doğrulanmadı** (PERF-007) — P1 teyit |
| Sanallaştırma (virtualization) | ⚠️ Doğrulanmadı |
| Sunucu tarafı filtreleme | ✅ `BuildWhere` + filtre grupları |

---

## Senaryo 7 — Mobil

**Değerlendirilemedi.** Ancak yapısal olarak:
- Sürükle-bırak tuval (`@xyflow/react`) mobilde kullanılabilir değildir.
- Desk'in tablo görünümleri mobilde çalışabilir.

**Öneri:** Mobil için hedefi **açıkça belirle**. "Mobilde canvas yok, Desk var"
demek, her şeyi yarım yamalak duyarlı yapmaktan iyidir. Bugün bu karar yazılı
değil.

---

## Senaryo 8 (ek) — Ekip üyesi ilk kez katılıyor

Denetimde eklendi çünkü ürün ekip iddiasında:

1. Davet linki (`/join/[token]`) ✅ var
2. Jeton hash'lenmiş saklanıyor ✅
3. Rol atanıyor ✅
4. **Yeni üye ne göreceğini biliyor mu?** ⚠️ Boş durum yok (UX-001)

---

## Elle doğrulama kontrol listesi

Bir sonraki oturumda uygulama açılıp şunlar tıklanmalı — her biri bu raporda
"doğrulanmadı" ile işaretli bir maddeyi kapatır:

- [ ] Kayıt → ilk ekran: ne yapacağı söyleniyor mu?
- [ ] `/demo` → kayıt: yapılan iş korunuyor mu?
- [ ] Canvas → Desk geçişi: gezinme var mı?
- [ ] Boş bir hesapta 7 Desk ekranı: ne görünüyor?
- [ ] `pageSize=100000` gönder: ne oluyor?
- [ ] DB'yi kapat, listeyi yenile: hata mesajı okunabilir mi?
- [ ] Mobil 375px: hangi ekranlar kullanılabilir?
- [ ] Yalnızca klavye ile bir tablo ekle
