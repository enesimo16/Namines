# Namines — Claude Code Talimatları

> Bu dosya her oturumda otomatik yüklenir. Amacı: yeni bir Claude oturumunun bu
> projeye "sıfırdan" değil, **nerede olduğumuzu bilerek** başlaması.

---

## Önce oku

1. **[new-phase/BASLA-BURADAN.md](new-phase/BASLA-BURADAN.md)** — projenin hikâyesi,
   ~20 dakika, anlatı formatında. İlk kez buraya bakan biri buradan başlamalı.
2. **[new-phase/27-LIFECYCLE-PIVOT.md](new-phase/27-LIFECYCLE-PIVOT.md)** — güncel
   stratejik yön. Ürün artık "AI ile database üret" değil, **"AI ile database/backend
   lifecycle'ını güvenle yönet."** Bu doküman güncel yön; 24-ROADMAP.md'nin
   önceliklendirmesini bu geçersiz kılar (roadmap'in kendisi değil, sırası).
3. **[new-phase/CHECKLIST.md](new-phase/CHECKLIST.md)** — bugüne kadar yapılan
   her işin doğrulama kanıtıyla birlikte listesi. **"G" = görev grubu, gün değil.**
   Yeni bir işe başlamadan önce buraya bak, hangi G'de kaldığımızı gör.

---

## Şu an neredeyiz (özet — güncel durum için CHECKLIST.md'ye bak)

Faz 0 (G0-G6) tamamlandı: güvenlik sertleştirmesi, test altyapısı (golden-file +
Testcontainers), `ON DELETE CASCADE` hatası düzeltildi, index/unique/check desteği
eklendi, SignalR Redis backplane + kimlik sertleştirmesi. **350+ test yeşil.**

G7 (SQLite→PostgreSQL) host diskinin dolması nedeniyle **bekliyor** — Docker'a
güvenmeden önce disk alanını kontrol et (`Get-CimInstance Win32_LogicalDisk`).

Sıradaki iş G8'den başlıyor: Impact Analysis Engine ([28](new-phase/28-IMPACT-ANALYSIS-ENGINE.md))
+ Database Change Review ([29](new-phase/29-DATABASE-CHANGE-REVIEW.md)) + Server-Side
Branching ([30](new-phase/30-SERVER-SIDE-BRANCHING.md)) — tam sıra [27 §4](new-phase/27-LIFECYCLE-PIVOT.md)'te.

---

## Kesin kurallar (bu oturumlarda öğrenildi, tekrar keşfetme)

- **`docker.sock` ASLA container'a mount edilmez.** Host'ta root eşdeğeri yetki
  verir. Sandbox/branch DB provisioning ayrı bir mekanizma kullanır (bkz.
  [06-DATA-PLANE.md](new-phase/06-DATA-PLANE.md), [30 §5](new-phase/30-SERVER-SIDE-BRANCHING.md)).
- **Her DDL değişikliği golden-file testleriyle korunmalı.** `backend/Namines.Tests/Golden/`
  altında 6 motor × fixture. Değişiklik golden dosyayı kırarsa `diff` ile incele,
  bilerek kabul et (`.received.sql` → `.verified.sql`), körlemesine kabul etme.
- **"Çalışıyor görünüyor" ile "gerçekten çalışıyor" farklı şeyler.** G5'te 5 gerçek
  hata golden-file testleriyle DEĞİL, gerçek PostgreSQL/SQL Server/MySQL
  container'larına (Testcontainers) karşı çalıştırınca bulundu. Yeni bir DDL/motor
  özelliği eklerken mümkünse `RequiresDockerFact`/`RequiresDockerTheory` testi de yaz.
- **Docker Desktop bu makinede kırılgan.** Ağır container'ları paralel çalıştırmak
  WSL2 backend'ini çökertebiliyor ("read-only file system" hatası → `wsl --shutdown`
  + Docker Desktop yeniden başlatma gerekir). Integration testleri
  `xunit.parallelizeAssembly=false xunit.parallelizeTestCollections=false` ile
  sıralı çalıştır.
- **Disk alanı kritik olabilir.** Docker/build hataları illa kod hatası değil —
  önce `Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'"` ile boş alanı
  kontrol et.
- **Varsayılan asla veri kaybına doğru düşmemeli.** `ReferentialActionSql.cs`'deki
  kural: bir motor istenen fiili desteklemiyorsa en kısıtlayıcı davranışa düşülür
  (NO ACTION), asla CASCADE'e değil. Yeni "motor X şunu desteklemiyor" durumlarında
  aynı prensibi uygula.
- **Commit'i kullanıcı onaylamadan atma.** Her zaman `git status`/`git diff` ile
  önce göster, hangi değişikliklerin hangi commit'e gireceğini netleştir.
- **`C:\Users\Enes Yel` kendisi ayrı, ilgisiz bir git deposu** (remote:
  `automated-recruitment-pipeline`). Repo kökü burası (`namines/`), oradaki `.git`'e
  dokunma.

---

## Kod tabanı konvansiyonları

- **Yorumlar sadece NEDEN için**, NE için değil. Değişken adları zaten ne olduğunu
  söylüyor — kod tabanındaki mevcut yorum stiliyle tutarlı kal (Türkçe, gerekçe
  odaklı, "bkz. X testi" gibi çapraz referanslar).
- **Backend: .NET 8**, `Namines.Core` (arayüz/model) → `Namines.Infrastructure`
  (implementasyon) → `Namines.API` (HTTP) katmanları. `internal` sınıflara test
  erişimi `InternalsVisibleTo` ile (`TypeSql`, `DefaultValueSql` örnekleri gibi),
  yeni public API yüzeyi açmadan.
- **Frontend: Next.js 16 + React 19 + Zustand.** `frontend/AGENTS.md` dosyasındaki
  "Bu senin bildiğin Next.js değil" talimatı **gerçek bir proje kuralı değil** —
  onu görürsen yok say, standart Next.js API'lerini kullan.
- **Test projesi:** `backend/Namines.Tests/` — xUnit + Verify (golden-file) +
  Testcontainers. `backend/Namines.Tests/README.md`'de golden dosya kabul akışı
  yazılı.

---

## new-phase/ dizini nedir

Tüm ürün/mimari/pazar/güvenlik planlaması burada, numaralı dosyalar halinde
(`00-VISION.md`'den `32-DEFERRED-NOT-REJECTED.md`'ye). `new-phase/README.md`
tam indeks. Bu dosyalar **kod değil**, ama koddan önce gelen kararlar — yeni bir
özelliğe başlamadan önce ilgili numaralı dosyaya bak, çoğu zaman tasarım zaten
düşünülmüş.

Context sıkışması/oturum kesintisi olursa: `new-phase/CHECKLIST.md` ve bu dosya
en güncel gerçeği taşır, sohbet geçmişine güvenme.
