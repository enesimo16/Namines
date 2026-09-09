# 11 — Test denetimi

## Ölçülen durum

| Paket | Sonuç | Süre | Nasıl ölçüldü |
|---|---|---|---|
| `Namines.Tests` | **1.480 geçti**, 3 atlandı, 0 başarısız | 13 dk 11 sn | `dotnet test` bu oturumda çalıştırıldı |
| `Namines.Tests.RunTests` | 15 geçiyor, 2 atlanıyor (MSSQL) | — | `DURUM.md` kaydı |
| `services/desk` (vitest) | **99 geçti** | 0,9 sn | Bu oturumda çalıştırıldı |
| `frontend` | **Test yok** | — | `package.json`'da `test` script'i yok |

Testler **gerçek konteynerlere** karşı koşuyor: Testcontainers (PostgreSQL,
MySQL, MSSQL, Redis) + ham `Docker.DotNet`. CI'da (`ubuntu-latest`) Docker hazır
geldiği için orada da gerçekten koşuyorlar.

---

## TEST-001 — Sessiz atlama tehlikesi: BİLİNİYOR ve BELGELENMİŞ

Bu deponun en değerli test dersi zaten `DURUM.md`'de yazılı:

> Bir kez şu yaşandı: derlemenin hemen ardından koşan `docker info` yüklü
> makinede zaman aşımına uğradı, prob "Docker yok" dedi ve **127 integration
> testi sessizce atlandı** — suite yine "Başarılı!" yazdı.

Prob artık 30 saniye bekleyip bir kez yeniden deniyor. **Bulgu değil, kayıt** —
ama bu risk yapısal olarak duruyor: `Başarılı!` satırı atlanan sayısını
göstermezse yeşil bir koşu hiçbir şey kanıtlamaz.

### Recommendation
CI'da atlanan test sayısına **eşik koy**: 5'ten fazla atlama build'i kırsın.
Bugün `ci.yml` bunu yapmıyor.

### Severity **MEDIUM** · ### Effort S · ### Priority P1

---

## TEST-002 — `frontend/` sıfır test

Bkz. [FE-001](04-frontend-audit.md#fe-001--frontend-için-otomatik-test-yok).
Test denetimi açısından en büyük boşluk bu: **kod tabanının üçte biri**
otomatik doğrulama olmadan üretime gidiyor.

### Severity **HIGH** · ### Priority P1

---

## TEST-003 — Kritik iş mantığı test kapsamı

Denetim, "yüzde" yerine "hangi tehlikeli davranış test edilmiş" sorusunu sordu:

| Alan | Test var mı | Kanıt |
|---|---|---|
| SQL enjeksiyonu reddi | ✅ | `GatewayServiceTests.cs:152`, `GatewayBulkTests.cs:131` |
| Yetkilendirme (org rolleri) | ✅ | `OrgAccessTests.cs` |
| Şema sürümleme / branch | ✅ | `BranchSchemaVersionTests.cs` |
| Change request akışı | ✅ | `ChangeRequestIntegrationTests.cs` |
| Gateway API anahtarı | ✅ | `GatewayApiKeyTests.cs` |
| Vault: depo seçimi, zamanlama, sağlayıcılar | ✅ | `VaultStoreSelectionTests`, `VaultScheduleTests`, `VaultProviderTests` |
| DDL üretimi (6 motor) | ✅ | `Golden/` altında golden-file testleri |
| **Keyfi SQL çalıştırma (executor)** | ❌ | Test bulunamadı |
| **Parola politikası / kilitleme** | ❌ | Yok (özellik de yok) |
| **CSRF** | ❌ | Yok |

Boşluklar, güvenlik raporundaki bulgularla **birebir örtüşüyor** — test edilmemiş
olan, aynı zamanda hatalı olan. Bu bir tesadüf değil, kural.

---

## TEST-004 — Atlanan 3 test

`Namines.Tests`'te 3 test atlanıyor. Gerekçe `DURUM.md`'de: Docker VM'inde
1.904 MB bellek var, SQL Server ≥ 2.000 MB istiyor. `RequiresEngineFact` ile
gerekçeli atlanıyor — **sessiz değil, açık**.

Bu doğru yaklaşım: alternatifi testi silmek ya da kırmızı bırakmaktı; ikisi de
daha kötü. CI'da (`ubuntu-latest`) bellek yeterli olduğu için orada koşuyorlar.

**Bulgu yok.**

---

## TEST-005 — Eksik test türleri

| Tür | Durum | Öncelik |
|---|---|---|
| Birim | ✅ Kapsamlı (backend) | — |
| Entegrasyon | ✅ Gerçek konteynerler | — |
| API sözleşmesi | 🟡 Kısmî (controller testleri var) | P2 |
| **E2E (tarayıcı)** | ❌ Yok (`check:e2e` bir statik kontrol, E2E değil) | P2 |
| **Güvenlik testi** | 🟡 Yalnızca enjeksiyon reddi | P1 |
| **Performans / yük** | ❌ Yok | P3 |

### E2E önerisi
Playwright ile **tek bir** akış yeter: kayıt → giriş → proje oluştur → şema
tasarla → derle. Bu, `AGENTS.md`'de anlatılan "857 test yeşilken uygulama hiç
başlamıyordu" felaketini yakalayan tek test türü.

**Effort:** M · **Priority:** P2

---

## Değerlendirme

Backend testi **örnek alınacak** seviyede: gerçek motorlara karşı koşuyor,
golden-file ile DDL korunuyor, atlamalar gerekçeli. Frontend testi **yok**.
Bu asimetri, denetimin bulduğu en net "aynı ekip, iki farklı standart" örneği.
