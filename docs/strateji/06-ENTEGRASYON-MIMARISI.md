# 06 — Entegrasyon mimarisi: Namines Connectors çerçevesi

> Supabase, Firebase, Neon ve diğerleri için **tek bir çerçeve**. Her yeni
> sağlayıcı aynı sözleşmeyi uygular. Web, masaüstü, eklenti ve mobil aynı
> bağlayıcıyı kullanır. Sağlayıcı listesi ve öncelikleri:
> [07 — Entegrasyon kataloğu](07-ENTEGRASYON-KATALOGU.md).

---

## 1. Bugünkü durum

- `Namines.Ground` içinde `IDatabaseProvider` var. Uygulamaları: `NeonProvider`, `SupabaseProvider`, `LocalPostgresProvider`.
  - Bu arayüz **Namines'in kendi hesabıyla** müşteri için DB **sağlamak** (provision) üzerine kurulu (`ProvisionSpec`, `ProvisionedDatabase`, `DatabaseMetrics`).
- **Kullanıcının kendi hesabını** bağlayıp onun projelerini keşfetmek, şemasını çekmek, Review'dan geçen değişikliği uygulamak için bir soyutlama **yok**. Bugün bu iş ham bağlantı dizesi yapıştırmakla yapılıyor (BYODB, Gateway).
- `docs/second-phase/18-...` §9 OAuth bağlayıcılarını ürün olarak tarif ediyor. Bu belge onun **mühendislik karşılığı**.

**Karar:** Ground'un `IDatabaseProvider`'ı "Namines'in sağladığı DB" olarak kalır. Yeni `Namines.Connectors` projesi "kullanıcının bağladığı hesap" içindir. İkisi karıştırılmaz: faturalama, sahiplik ve silme semantikleri farklı.

## 2. Bağlayıcı sözleşmesi

Her bağlayıcı, desteklediği **yetenekleri** bildirir. UI bu listeye göre düğme gösterir; desteklenmeyen işlem için düğme hiç çıkmaz.

| Yetenek | Anlamı | Örnek |
|---|---|---|
| `Auth` | Hesap bağlama (OAuth2/PKCE, scoped token, service account) | Supabase OAuth, Cloudflare API token |
| `Discover` | Organizasyon, proje, veritabanı ve branch listesi | "Supabase projelerin: …" |
| `Credentials` | Kısa ömürlü bağlantı bilgisi alma (mümkünse parola saklamadan) | Neon branch bağlantı dizesi, AWS IAM token |
| `Introspect` | Canlı şemayı Namines IR'a çekme | `information_schema` / Management API |
| `Apply` | Review'dan **geçmiş** DDL'i uygulama | Supabase SQL / migration API |
| `Branch` | Sağlayıcının branch'ini Namines branch'ine eşleme | Neon, PlanetScale, Supabase branching |
| `Backup` | Vault yedek kaynağı olma | Postgres/MySQL dump; Firestore export |
| `Policies` | Güvenlik politikası okuma ve yazma | Supabase RLS, Firestore security rules |
| `Webhooks` | Sağlayıcıdan gelen olaylar (şema değişti, deploy) | PlanetScale deploy request webhook |
| `Metrics` | Boyut, bağlantı, kota | Ground'daki `DatabaseMetrics` ile aynı biçim |

Taslak arayüz (C#; isimler uygulama anında netleşir):

```csharp
public interface IConnector
{
    string Key { get; }                       // "supabase", "firebase", ...
    ConnectorCapabilities Capabilities { get; }
    Task<AuthStart> BeginAuthAsync(AuthContext ctx, CancellationToken ct);
    Task<ConnectorAccount> CompleteAuthAsync(AuthCallback cb, CancellationToken ct);
    IAsyncEnumerable<ConnectorResource> DiscoverAsync(ConnectorAccount acc, CancellationToken ct);
    Task<DatabaseSchema> IntrospectAsync(ConnectorAccount acc, ResourceRef res, CancellationToken ct);
    Task<ApplyResult> ApplyAsync(ConnectorAccount acc, ResourceRef res, ApprovedChange change, CancellationToken ct);
    // Diğer yetenekler ayrı arayüzlerde: IBranchingConnector, IPolicyConnector, IBackupSource ...
}
```

**`ApplyAsync` yalnızca `ApprovedChange` kabul eder.** Bu tip, onaylanmış bir Change Request'ten başka bir yerde üretilemez (kurucu `internal`, tek fabrika `ChangeRequestService`). Böylece "bağlayıcı üzerinden Review'ı atlamak" derleme zamanında imkânsız olur. Bu, ürünün kama ucunu (güvenli değişiklik) mimariye gömmektir.

## 3. Hesap ve token yönetimi

- **`ConnectorAccounts` tablosu:**
  - `OrganizationId`, `ConnectedByUserId`, `Provider`, `ExternalAccountId`, `Scopes`, `EncryptedTokens`, `ExpiresAt`, `RevokedAt`.
  - Şifreleme mevcut `Security:ConnectionEncryptionKey` ve DataProtection ile; Desk'in saklı bağlantılarıyla aynı yöntem.
- **Sahiplik:** Hesap organizasyona bağlanır, ama bağlayan kullanıcı kaydedilir. Bağlayan organizasyondan çıkarılırsa veya Owner/Admin yetkisini kaybederse **hesap askıya alınır** ve Admin'e "yeniden bağla" bildirimi gider. Bu, denetimde API anahtarlarına uygulanan kuralın (A-8) aynısı.
- **Yenileme:** Arka plan görevi süresi yaklaşan token'ları yeniler. Başarısızlıkta hesap `NeedsReauth` durumuna geçer, sessizce bozuk kalmaz.
- **En dar scope:** Her sağlayıcı için önce salt-okuma scope'u istenir. `Apply` için ek scope, **ilk uygulama anında** artımlı (incremental) yetki ile istenir.
- **Parola yerine kısa ömürlü kimlik:** Sağlayıcı destekliyorsa (Neon branch rolleri, AWS IAM auth, GCP IAM DB auth) kalıcı DB parolası saklanmaz.
- **Ağ kuralları:** Bağlayıcı HTTP çağrıları `IHttpClientFactory` + mevcut Polly resilience pipeline'ları (`NaminesResiliencePipelineNames`) ile yapılır. DB bağlantıları mevcut SSRF guard ve egress allowlist'ten geçer; sağlayıcı alan adları allowlist'e bağlayıcıyla birlikte eklenir.

## 4. IR eşleme katmanı

Her bağlayıcı sağlayıcının modelini **Namines kanonik IR'ına** (`ir.json` / `DatabaseSchema`) eşler. Ortak kurallar:
1. **Sistem şemaları dışarıda:** Supabase'de `auth`, `storage`, `realtime`, `vault`, `extensions`; Postgres'te `pg_catalog`, `information_schema`. Bu şemalara giden FK'ler "dış referans" olarak işaretlenir (silinmez, değiştirilmez).
2. **Bilinmeyen tip** IR'da `raw` tip olarak taşınır ve UI'da uyarı gösterilir. Sessizce `text`'e çevrilmez; denetimdeki "varsayılan asla veri kaybına doğru düşmez" kuralı.
3. **Politikalar** (RLS, security rules) şemadan ayrı, IR'ın `policies` bölümünde tutulur. Diff ve risk motoru bunları da karşılaştırır. "RLS kapatıldı" bir **Breaking** değişikliktir.
4. **Şemasız kaynaklar** (Firestore, MongoDB) için IR, **çıkarılmış şema** olarak işaretlenir (`inferred: true`, örnek sayısı, güven oranı). Bu şemalar "uygula" yerine "doğrulama kuralı / security rules üret" akışına gider.

## 5. Test stratejisi: bağlayıcı uygunluk kiti

Her bağlayıcı aynı test kitinden geçer:
- **Kayıtlı yanıt testleri** (VCR benzeri): sağlayıcı API yanıtları fixture olarak saklanır ve her PR'da çalışır, ağ gerekmez.
- **IR golden testleri:** örnek sağlayıcı şeması → beklenen IR. Mevcut golden-file altyapısı kullanılır.
- **Canlı duman testi (gece):** sağlayıcının ücretsiz katmanında Namines'e ait test hesabı (keşif → introspect → geri alınabilir DDL → geri al). Başarısız olursa alarm üretir; PR'ı bloklamaz.
- **Güvenlik testleri:**
  - token log'a yazılmıyor,
  - bağlayan kullanıcı çıkarılınca hesap askıya alınıyor,
  - `ApplyAsync`'e onaysız değişiklik verilemiyor (derleme + çalışma zamanı).

## 6. UI: "Bağlantılarım" ekranı

- Web'de `Settings → Connections`. Masaüstü ve eklenti aynı API'yi kullanır.
- **Her bağlayıcı kartında:** durum (bağlı / yeniden yetki gerekli / askıda), scope'lar, bağlayan kişi, son kullanım, "bağlantıyı kes".
- **Kesme işlemi** sağlayıcı tarafında da token'ı iptal eder (revocation endpoint varsa). Yoksa kullanıcıya "sağlayıcı panelinden de kaldırın" bağlantısı gösterilir.

## 7. Efor

| İş | Efor |
|---|---|
| `Namines.Connectors` iskeleti, hesap tablosu, şifreleme, yenileme görevi, OAuth geri dönüş uçları, UI ekranı | 2-3 hafta |
| İlk bağlayıcı (Supabase), uygunluk kiti ile | 2 hafta |
| Sonraki her Postgres/MySQL tabanlı bağlayıcı | 1-1.5 hafta |
| Şemasız kaynak (Firestore, MongoDB) | 2-3 hafta (çıkarım motoru dahil) |
