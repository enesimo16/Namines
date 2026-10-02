# Namines — nerede kaldık

> Kısa durum özeti. Ayrıntı için: [AGENTS.md](AGENTS.md) ·
> [docs/new-phase/CHECKLIST.md](docs/new-phase/CHECKLIST.md) (iş tarihçesi) ·
> [docs/last-review/](docs/last-review/) (son güvenlik turu).
>
> Son güncelleme: 2026-10-02 (v3)

---

## Ürün tek cümlede

AI ile database/backend **lifecycle'ını güvenle yönetmek** — şema üretmek değil.
Stratejik yön: [docs/new-phase/27-LIFECYCLE-PIVOT.md](docs/new-phase/27-LIFECYCLE-PIVOT.md).

## Bugün ne çalışıyor

- **Studio** (canvas, şema tasarımı, AI üretimi), **Compile** (6 motor DDL + eject)
- **Change Review** — risk sınıflandırması, etki analizi, onay akışı, gerçek
  container'da "Run Tests"
- **Gateway** — şemadan otomatik REST (okuma + yazma), API anahtarı izin modeli,
  PII maskeleme, denetim kaydı
- **Desk** (veri yönetim paneli), **Vault** (şifreli yedek + geri yükleme),
  **Ground** (yönetilen DB), **Launch**, **Flow** (otomasyon)
- **MCP sunucusu + CLI + Claude Skill**, **GitHub botu** (PR'a şema incelemesi)
- İki dilli arayüz (EN/TR)

## Son tur: ikinci güvenlik incelemesi (G59)

63 bölümlük çekişmeli bir denetim prompt'uyla bulunan **30 bulgunun tamamı kapandı**:
1 CRITICAL · 7 HIGH · 17 MEDIUM · 5 LOW.

**En ciddi üçü:**
- **C-1** — altı DDL üreticisinin `Quote`'u ayırıcıyı ikilemiyordu ve şema doğrudan
  istemciden geliyordu; üretilen betik sandbox'ta superuser olarak çalışıyor.
- **H-4** — ekipten çıkarılan üyenin API anahtarı süresiz çalışıyordu ve istekler
  hâlâ onun hesabına faturalanıyordu.
- **M-17** — üretilen CoderAI paketlerine aynı sabit parola gömülüyordu ve DB portu
  `0.0.0.0`'a açılıyordu (genel IP'li makinede: veritabanını internete açmak).

**Tek kısmi kalan:** M-15 — Desk devir teslim jetonunun ömrü 7 günden 12 saate
çekildi ve işaretlendi, ama hâlâ projeye bağlı değil. Gerçek kapsamlama her ucun
claim'e göre gözden geçirilmesini gerektiriyor.

Düzeltmelerin kendisi **4 hata üretti** (R-1…R-4); dördü de bulundu ve kapatıldı.
İkisini mevcut testler, birini localhost doğrulaması, birini de "hangi binary
koşuyor?" sorusu yakaladı.

### Duyurulması gereken 4 davranış değişikliği

1. **Admin'ler artık ham-SQL / toplu-dışa-aktarım API anahtarı üretemiyor** — Owner işi.
2. **`AllowDeskSql` varsayılan kapalı olduğu için**, saklı bağlantıda ham SQL
   çalıştıran *mevcut* bir API anahtarı 403 alır. Owner proje ayarlarından açar.
3. **Üretimde `ForwardedHeaders:KnownNetworks` tanımsızsa uygulama başlamıyor.**
   Ya proxy CIDR'ları verilecek ya `ForwardedHeaders:TrustAllProxies=true` ile risk
   açıkça kabul edilecek (aksi hâlde login hız sınırı atlatılabiliyordu).
4. **MCP artık genel internet veritabanı hostlarına bağlanmıyor.** Neon/RDS/Supabase
   için `Security:McpAllowPublicDbHosts=true` (bkz. `backend/Namines.Mcp/README.md`).

### Yeni yapılandırma anahtarları

| Anahtar | Varsayılan | Ne yapar |
|---|---|---|
| `ForwardedHeaders:TrustAllProxies` | `false` | `X-Forwarded-For`a doğrulamasız güvenmeyi açıkça kabul eder |
| `Vault:AllowLegacyV1` | `false` | Eski NVLT1 yedeklerinin okunmasına izin verir |
| `Security:McpAllowPublicDbHosts` | `false` | MCP'nin genel internet DB hostlarına bağlanmasına izin verir |
| `Security:McpAllowedDbHosts` | *(boş)* | MCP için hedef allowlist (tanımlıysa tek kapı) |
| `Sandbox:OutputsDirectory` | `./Outputs` | Sandbox çıktılarının yeri (temizlik servisi burayı süpürür) |
| `Redis:ConnectionString` | *(boş)* | Tanımlıysa Gateway hız limiti **dağıtık** sayaç kullanır |

### Veritabanı

Bu turda **4 EF migration** eklendi, hepsi yalnızca ekleme yapıyor ve gerçek
PostgreSQL'e uygulandı:
`AddStripeWebhookEventIdempotency` · `PinVotingMembersOnChangeRequest` ·
`CacheChangeRequestAiOutputs` · `BindVaultRecordToCipherText`.

> **Dağıtımda:** `dotnet ef database update` (ya da `dotnet run -- --migrate`).

## Doğrulama durumu

| Koşu | Sonuç |
|---|---|
| Backend suite | **2324 / 2324** |
| `Namines.Tests.RunTests` (gerçek Docker) | **19 / 19** |
| `dotnet build` (tüm çözüm) | 0 hata |
| Uygulama başlatma | istisna yok |

Localhost'ta test hesabıyla uçtan uca denendi: C-1 enjeksiyonu nötr + geçersiz ad
400, M-9 anonim yanıtı yalnızca `{ok}`, L-1 `GET` hiçbir şey oluşturmuyor,
M-15 jetonu 12 saat (normal giriş hâlâ 7 gün).

## Sırada ne var

1. **Cache katmanı — kiracı bağlamı.** Hiç denetlenmedi ve bir cache anahtarında
   org/kullanıcı bağlamının eksik olması klasik bir çapraz-kiracı sızıntısıdır.
   Bir sonraki güvenlik turunun 1 numarası:
   [docs/last-review/01-INCELENMEYENLER.md](docs/last-review/01-INCELENMEYENLER.md).
2. **DDL üreticilerinin enjeksiyon dışındaki yüzeyi** — bu turda yalnızca
   tanımlayıcı alıntılaması incelendi; `DefaultValueSql`, check/generated ifadeleri,
   `COMMENT ON`, rezerve kelimeler hâlâ bakılmadı.
3. **M-15'in kapsamlaması** — Desk jetonunu projeye bağlamak.
4. **Hesap/karar bekleyenler:**
   [docs/new-phase/34-SENDEN-BEKLENENLER.md](docs/new-phase/34-SENDEN-BEKLENENLER.md)
   (Stripe anahtarları, GitHub App, npm yayını, kalıcı Groq anahtarı).
5. **Sıradaki büyük başlıklar:**
   [docs/new-phase/35-KALAN-BUYUK-ISLER.md](docs/new-phase/35-KALAN-BUYUK-ISLER.md).

## Bu kod tabanında öğrenilen en pahalı dersler

- **"Testler geçiyor" hiçbir şey kanıtlamıyor.** G39'da 857 test yeşilken uygulama
  hiç başlamıyordu. Yeni bir DDL ya da uç eklerken **gerçek motorda çalıştır ve
  uygulamayı ayağa kaldır**.
- **"Yeşil" demeden önce hangi testlerin hangi değişikliği kapsadığına bak.** Bu
  turda iki kez, değişiklikten ÖNCE başlamış bir suite'in sonucunu "yeşil" diye
  raporladım; ikisinde de regresyon vardı.
- **Bir testin geçmesi, DOĞRU ŞEYDEN geçtiği anlamına gelmiyor.** M-8'de testler
  yeşildi ama bağlamayı ölçmüyorlardı — kanıt bir uzunluk tesadüfünden geliyordu.
  Mutasyon testi (düzeltmeyi geri al, testin kırmızıya döndüğünü gör) bunu açığa
  çıkardı.
- **Derleme çıktısında `error CS` aramak yetmez.** Dosya kilidi hataları `MSB****`
  koduyla gelir ve `--no-build` bayat bir DLL koşturur. Açıklanamaz bir test
  davranışında ilk soru: *hangi binary koşuyor?*
