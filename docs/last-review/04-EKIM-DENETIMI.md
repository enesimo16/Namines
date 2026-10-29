# Ekim denetimi — son 1 ayın commit'leri (2026-09-28 → 2026-10-27)

> Dal: `fix/october-audit` (main'den, push edilmedi). 44 commit incelendi
> (~87k satır), 6 paralel inceleme + 10 düzeltme ajanı. 72 düzeltme commit'i.

## Başlangıç durumu

- **CI bir aydır hiç yeşil olmadı.** Backend job'ı NuGet güvenlik taramasında
  (7 paket High CVE), frontend job'ı lint'te (15 hata) düşüyordu. Bu yüzden
  build/test adımları CI'da hiç koşmuyordu.
- Lint hatalarının 4'ü gerçek **çalışma zamanı çökmesiydi** (rules-of-hooks):
  `WorkspaceNav` sayfa geçişlerinde, `/flow` girişten sonra çöküyordu.

## En ciddi bulgular (hepsi kapatıldı)

| # | Seviye | Sorun |
|---|---|---|
| 1 | CRITICAL | `POST /api/auth/oauth/callback {isDevSimulation:true}` üretimde herkese ortak bir hesapla oturum açtırıyordu. |
| 2 | CRITICAL | MCP `namines_desk_execute_query` her SQL'i (DROP/UPDATE dahil) korumasız çalıştırıyordu. |
| 3 | HIGH | Google id_token `aud`/`email_verified` kontrolsüz; OAuth 2FA ve kilitlemeyi atlıyordu; önceden kayıt ile hesap ele geçirme. |
| 4 | HIGH | Stripe webhook: handler hata verirse olay "işlendi" kalıyor, ödeme yapan müşteri yükseltilmiyordu. |
| 5 | HIGH | C-1 yarımdı: Generated/DEFAULT/CHECK/index WHERE/enum/tip adları sandbox betiğine ham giriyordu (`\!` ile kabuk). |
| 6 | HIGH | CanvasHub: başarısız `JoinRoom` sonrası Viewer şema yazabiliyordu; frontend'in `room-` öneki branch ACL'ini tamamen atlatıyordu. |
| 7 | HIGH | Ekipten çıkarılan kurucu projeyi yazabiliyor/çekip alabiliyordu; yeni projeler kendiliğinden takıma paylaşılıyordu. |
| 8 | HIGH | Hub: Editor görünürlüğü Public yapabiliyordu; anonim GetRepo sahibin e-postasını sızdırıyordu. |
| 9 | HIGH | Feature flag kill-switch'i ne backend'de ne frontend'de uygulanıyordu (flag'ler hiç yüklenmiyordu). |
| 10 | HIGH | Admin/Hub UI'da sahte başarı: sunucu hatasında "kaydedildi", sahte sağlık skorları, uydurma istatistikler. |
| 11 | HIGH | Desk: `?token=` ile login-CSRF; prod compose'da backend hiç başlamıyordu (ForwardedHeaders). |

Ayrıntılı bulgu listesi her commit mesajının gövdesinde.

## Doğrulama (bu makinede, Docker yok)

- Backend: build 0 hata; `dotnet list package --vulnerable` temiz;
  **2517 test geçti**, 145 atlandı, 8 başarısız. 8'i Docker isteyen
  integration testi (`ByodbHardeningTests`×2, `GatewayApiKeyTests`×6) ve
  **değişiklik öncesi koda karşı da aynı şekilde başarısız** — ortam kaynaklı.
- CLI çıkış kodu duman testi (CI'daki): geçti.
- Frontend: `tsc` 0, `eslint` 0 hata, design-rules temiz, vitest 264/264,
  `next build` geçti, `npm audit --audit-level=high` temiz (next 16.4.3).
- Desk: typecheck, 121/121 test, `next build`, `npm audit` 0.
- **GitHub CI (push sonrası): dört job da YEŞİL** — Docker'lı integration
  testleri ve `Namines.Tests.RunTests` (19/19) dahil. Bu koşu, CI'ın bugüne
  kadar hiç ulaşamadığı üç hatayı ortaya çıkardı; üçü de düzeltildi:
  MSSQL Run Tests konteyneri ağsız açılmıyordu, Linux'ta Vault yedek konteyneri
  127.0.0.1'e yayımlanmış porta ulaşamıyordu (Launch'ın ilk yedeği hep
  başarısızdı), CLI duman testi `bash -e` yüzünden beklenen çıkış kodunda ölüyordu.

## Davranış değişiklikleri (duyurulmalı)

1. OAuth dev simülasyonu yalnızca Development + `Auth:AllowOAuthDevSimulation=true`
   (dev compose'da açık). 2FA'lı hesaplar OAuth ile giremez (403); onaysız
   e-postalı mevcut hesaba OAuth ile girilirse parola sıfırlanır.
2. Takım paylaşımı artık yalnızca açık `share` ile; Share için Admin/Owner,
   Unshare için Owner gerekir.
3. Ham-SQL/export API anahtarları, oluşturan Owner olmaktan çıkınca çalışmaz.
4. Kapatılan feature flag uçları 503 `FEATURE_DISABLED` döner.
5. MCP desk sorgusu salt-okunur, SQLite desteklenmez; MCP guard çakışan host
   anahtarlarını reddeder.
6. DDL: `\`, `--`, `/* */`, `;` içeren ifadeler 400 döner (çok satırlı ifadeler
   tek satıra indirilir). Motor sınırını aşan kısıt adlarına 8 haneli hash
   eklenir (yalnızca 3 Oracle golden dosyası değişti).
7. Stripe webhook handler hatasında 500 döner → Stripe tekrar dener.
8. Admin uçları `dev` planı veremez; admin ekranları sahte veri yerine 0/boş gösterir.
9. Prod: Caddy'de yalnız `/health/live` açık; konteynerler root olmayan
   kullanıcıyla koşar — mevcut `namines-app-data` volume'u için bir kez
   `chown -R app:app /app/data` gerekir (API Dockerfile yorumunda).
10. `namines-backup` DB dökümü yoksa hata verir; `namines-update` başarısız
    build'i bir sonraki koşuda yeniden dener.

## Açık kalanlar

- **A-10:** Ground `Deleting/Provisioning` takılı claim'leri geri almak için
  `ClaimedAt` kolonu + migration gerekiyor (eklenmedi).
- **Hub `(UserId, Slug)` unique index** migration'ı eklenmedi; slug üretimi
  artık kullanıcı başına benzersiz.
- In-app `/desk` sayfası örnek veri gösteriyor (artık açıkça etiketli);
  `/api/gateway/desk-sql`'e bağlanmadı. Caddy arkasında `/desk*` hâlâ Desk
  servisine gidiyor.
- Bot denetimindeki SignalR/AI Gateway/Docker probları hâlâ koşulsuz "pass".
- `ExceptionMiddleware`'de `UnauthorizedAccessException → 401` eşlemesi duruyor.
- Bir commit (`fcf8fc9`) tek başına derlenmez (bir sonraki commit'le tamamlanır).
