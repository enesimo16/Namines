# Namines — Kalan Büyük İşler (35-KALAN-BUYUK-ISLER.md) + third-phase/Desk Güncel Durum

Bu dosya "yarım bırakılmış iş" listesi değil — her biri bilinçli olarak sonraya bırakılmış ayrı bir başlık, `CHECKLIST.md`'deki her gate (G) maddesinin "Kapsam dışı" notlarının toplamı. Son güncelleme: Gate G52.

## Öneri sırası (new-phase doc'unun kendi tablosu)

0. Stripe'ta dört fiyat + disk açmak (kod değil, hesap/karar — bkz. 09-blocked-on-user.md)
1. Namines Bot'un kalanı (PR'da önizleme veritabanı, `/namines plan|preview|approve` komutlarının gerçekten çalışması)
2. Ekibin derinleşmesi (aynı şema üzerinde canlı birlikte düzenleme — SignalR altyapısı hazır, ekip modeline bağlanmadı)
3. GraphQL (Redis kararına bağlı)
4. NSL'in kalanı (şema adı, `@ui`/`@tag`, view, RLS, Migration IR, WASM derlemesi)
5. Console'un kalanı (dashboard motoru, doğal dil sorgu, özelleştirme katmanı)
6. Gözlemlenebilirlik/faturalama kuyruğu + Blueprint Hub (dış servis/içerik ağırlıklı, kod tarafı en hafif)

## Bilerek yapılmayacaklar (ertelenmedi, reddedildi)

- `docker.sock`'un container'a mount edilmesi — host'ta root eşdeğeri yetki verir.
- Bot'un yanlış yazılmış bir komutu tahmin etmesi (`aprove` → `approve`) — yıkıcı bir değişikliğin yazım hatasıyla onaylanması demek.
- Bir motorun desteklemediği referans fiilinde CASCADE'e sessizce düşmek — veri kaybı demek.
- Gateway'in `expand` (ilişki gömme) özelliği oturum yolunda çalışmaz durumda kaldı: çalışma zamanında şema bilgisi ister ama Gateway durumsuz; yalnızca bir kimlik yolunda çalışan bir özellik hiç olmamasından kötü sayıldı.

## Data Plane (06-DATA-PLANE.md) — Ground'un new-phase'teki karşılığı, hâlâ eksik

Bugün var: branch başına gerçek bir yerel PostgreSQL (docker.sock mount edilmeden), TTL, rastgele parola. **Eksik:** Neon copy-on-write branch'leri (Neon hesabı bekliyor), MinIO/S3'e yedek, Namines Bridge (on-prem tünel agent), veri düzlemi PII maskeleme, plan bazlı kotalar, Vault ile kimlik saklama. ⚠️ Bugünkü sağlayıcı yalnızca **yerel geliştirme veritabanı** üretir, prod verisi için değildir.

## third-phase — Ground / Vault / Desk güncel gerçek durum (kod incelemesiyle doğrulandı)

- **Namines Ground:** Hiç kod yok. `services/` altında klasörü bile yok. En büyük ve en riskli iş (7/24 nöbet, kötüye kullanım, veri kaybı sorumluluğu). Öneri: kendi Postgres'i işletmek yerine Neon'u arkada sağlayıcı olarak kullanıp üstüne kota/izolasyon katmanı yazmak.
- **Namines Vault:** Kod var (`DockerBackupService.cs`) ama **yanlış şeyi** yedekliyor — DDL'den geçici bir container kurup o boş container'ı yedekliyor (şema yedeği, veri yedeği değil). Yapılması gereken: kullanıcının canlı veritabanına bağlanıp gerçek `pg_dump`/`mysqldump`/`BACKUP DATABASE` çalıştırmak.
- **Namines Desk v0.1:** Şu ana kadar kodlanan tek şey. Deterministik CRUD (liste/ekle/güncelle/sil), API anahtarıyla, gerçek PostgreSQL'e karşı tarayıcıdan uçtan uca doğrulanmıştı (2026-09-01).
- **Namines Desk D1 (Kimlik + Oturum) — bu oturumda kodlandı:** API anahtarı yerine JWT oturumu. Backend'de `GatewayController.ResolveConnectionAsync`'e `projectId` + `OrgAccess.CanViewAsync`/`CanEditAsync` doğrulama dalı eklendi (dokümanın önerdiği `CanManageMembersAsync` yerine — o Admin/Owner'a kilitli, normal bir Viewer/Editor'ı kendi projesinden dışlardı). `GET /api/gateway/schema` artık `?projectId=` ile oturum yolunu kabul ediyor. Desk frontend'inde `lib/auth.ts` (login, sessionStorage, fetchProjects) eklendi, `lib/api.ts` `DeskSession{token,projectId}`'e geçirildi. `dotnet build` temiz, tam test paketi (1356 test) kırılmadan geçti, Desk `tsc`/`next build` temiz. **Canlı doğrulama (gerçek hesapla giriş, iki kullanıcıyla çapraz-proje 403 testi) henüz yapılmadı** — projenin "test geçti kanıt değil" kuralına göre bu adım kullanıcı tarafından koşulmalı.
- **Desk yol haritasında sıradaki adımlar (namines_desk/09-YOL-HARITASI.md):** D2 (Projects + Import — kart ızgarası, bağlantı durumu, gerçek bağlanma testi), D3 (Canvas — salt-okunur şema + FK çizgileri + drift rozeti), D4 (Data v1 — FK açılır liste, filtre/sıralama), D5 (Deployments okuma), D6 (Logs), D7 (Analytics). Ardından Vault, ancak ondan sonra "şemayı veritabanına uygula" özelliği.

## Bilinçli olarak v1 dışı bırakılan Desk özellikleri

DDL'i canlı veritabanına uygulama (Vault'tan sonra), GitHub push entegrasyonu (GitHub App hesabı bekliyor), okuma logu (hacim/saklama/maliyet tasarlanmadı), proje bazlı kullanım/fatura (`UsageEvent`'te `ProjectId` yok), API anahtarı yönetim ekranı (v1.1), SSO devri (v1.1), canlı log akışı (v1.1).
