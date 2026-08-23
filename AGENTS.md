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
4. **[FRONTEND.md](FRONTEND.md)** — `frontend/` altında görsel/UX işi yapıyorsan
   ÖNCE bu dosyayı oku. Sabit renk paleti, tipografi, `ui-ux-pro-max` skill
   kullanım zorunluluğu ve kütüphane kuralları burada.

---

## Şu an neredeyiz (özet — güncel durum için CHECKLIST.md'ye bak)

Faz 0 (G0-G7) tamamlandı: güvenlik sertleştirmesi, test altyapısı (golden-file +
Testcontainers), `ON DELETE CASCADE` hatası düzeltildi, index/unique/check desteği
eklendi, SignalR Redis backplane + kimlik sertleştirmesi, **control DB SQLite'tan
PostgreSQL'e tam geçiş** (gerçek Postgres'e karşı doğrulandı, health check yeşil).
G8-G17 de tamamlandı: `SchemaImpactAnalyzer`, migration risk sınıflandırması, sunucu-taraflı
branch modeli, Database Change Review UI (`/review`), "Run Tests" (gerçek ephemeral
container'a karşı DDL doğrulama — Testcontainers değil, ham `Docker.DotNet`, bkz. CHECKLIST
G12 notu), "Affected Code" statik etki taraması, Minimal Gateway (kullanıcının canlı
DB'sine karşı salt-okunur liste+detay REST — `DbIntrospectController` ile aynı SSRF/
no-persistence güvenlik modeli), AI Impact Explainer (`ImpactReport`'u insan diline
çeviren, kendi bulgu üretmeyen bir ajan), Safe risk otomatik onayı + ChangeRequest
audit log'u (proje-bazlı opt-in, `ChangeRequestAuditLog`), ve CanvasHub'ın `roomId`'sinin
sunucu-otoriteli branch_id'ye bağlanması (kimliği doğrulanmış + aktif projesi olan
kullanıcılar artık rastgele oda yerine gerçek branch'lerinde buluşuyor, guest akışı
korunuyor).

**G18-G49 de tamamlandı** — özet: MCP sunucusu + CLI + Claude Skill, 19 eject
hedefi (TypeScript SDK ve çalışan bir Next.js yönetim paneli dahil), Gateway'in
tam yazma yüzeyi (`create/update/delete/import/rpc/query/query-nl`) + API anahtarı
izin modeli + denetim kaydı, üretilen panelde yazma ekranları ve rol modeli,
şema modelinin genişlemesi (`identity`, enum, `generated`, `collation`, dizi),
kanonik JSON IR (`ir.json`), Namines Bot'un GitHub'a yazması, sosyal önizleme
sayfaları ve gözlemlenebilirlik/faturalama ölçümü.
**1052 test yeşil** (+ ayrı `Namines.Tests.RunTests/` projesinde 6
gerçek-Docker testi).

> **Bu oturumlarda öğrenilen en pahalı ders:** "testler geçiyor" hiçbir şey
> kanıtlamıyor. G39'da 857 test yeşilken **uygulama hiç başlamıyordu**; G44'te
> Türkçe kültür hatası geliştirme makinesinde üretimdeydi; G45'te iki hata
> yalnızca gerçek PostgreSQL/SQLite'a karşı çalıştırınca çıktı. Yeni bir DDL
> ya da uç eklerken **gerçek motorda çalıştır ve uygulamayı ayağa kaldır**.

Kalan işler iki dosyada toplu hâlde duruyor:
[34-SENDEN-BEKLENENLER.md](new-phase/34-SENDEN-BEKLENENLER.md) (kod hazır, bir
hesap/karar bekliyor) ve [35-KALAN-BUYUK-ISLER.md](new-phase/35-KALAN-BUYUK-ISLER.md)
(sıradaki büyük başlıklar + önerilen sıra). Yeni bir işe başlamadan önce 35'e bak.

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
- **"SQLite kaldırıldı" demek yanıltıcı — iki ayrı SQLite kullanımı var.** G7'de
  kaldırılan, control DB'nin ORM sağlayıcısıydı (`Microsoft.EntityFrameworkCore.Sqlite`).
  `Microsoft.Data.Sqlite` paketi hâlâ duruyor ve duracak — `DatabaseExecutorService`/
  `ScaffolderService`'te kullanıcının hedef motor olarak SQLite seçebilmesi (6 motordan
  biri) için gerekli, control DB ile ilgisi yok. İkisini karıştırma.
- **`.gitignore`'da genel bir `*.md` kuralı var**, `!README.md`/`!CLAUDE.md`/
  `!new-phase/*.md` istisnalarıyla. Yeni bir kök-dizin veya alt-dizin markdown
  dosyası eklersen (new-phase dışında) `git status`'ta gerçekten göründüğünü
  doğrula — sessizce yutulabilir (bir kere oldu, `49bc637` bunu düzeltti).
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
