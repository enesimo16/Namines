# 13 — Güvenlik & Uyumluluk

Faz 1'de ciddi güvenlik çalışması yapılmıştı (JWT httpOnly cookie, SSRF guard, AES-256-GCM BYOK, rate limiting, prompt injection sertleştirme). Bunlar **korunur**. Bu doküman eksikleri kapatır ve çok kiracılı bir platformun gerektirdiği seviyeye çıkarır.

---

## 1. Tehdit modeli (STRIDE)

| Tehdit | Senaryo | Önlem |
|---|---|---|
| **Spoofing** | Sahte JWT ile başka kullanıcı olma | Güçlü `Jwt:Key` (prod'da zorunlu, Faz 1'de yapıldı ✔), kısa TTL, refresh rotasyonu, key rotation |
| **Tampering** | Kullanıcı başka projenin şemasını değiştirme | Her istekte `organization_id` + proje sahipliği doğrulaması, control DB'de RLS |
| **Repudiation** | "Ben o kaydı silmedim" | Değiştirilemez audit log (Console + control plane) |
| **Information disclosure** | Kiracı A, kiracı B'nin verisini görme | **DB-per-project izolasyonu** + Gateway'de rol filtresi + DB'de RLS (iki katman) |
| **DoS** | Pahalı AI/provisioning uçlarını yağmalama | Rate limit (kullanıcı partitionlı — Faz 1 ✔), kota, iş kuyruğu, sorgu timeout, kaynak limitleri |
| **Elevation of privilege** | Sandbox'tan host'a kaçış | **docker.sock kaldırıldı**, gVisor, düşük yetkili DB kullanıcıları, NetworkPolicy |

### Faz 1'e özel bulgular ve kapanışları

| Bulgu | Şiddet | Kapanış |
|---|---|---|
| `docker.sock` container'a mount ediliyor → host root | 🔴 Kritik | Kaldırıldı; K8s Job broker + gVisor ([06](06-DATA-PLANE.md)) |
| SignalR hub'ında kimlik doğrulama yok | 🔴 Kritik | Bağlantıda JWT zorunlu ([10](10-REALTIME-COLLAB.md)) |
| roomId sızıntısı = süresiz erişim | 🟠 Yüksek | Süreli, iptal edilebilir paylaşım token'ları |
| `ForwardedHeaders` `KnownProxies` boş | 🟠 Yüksek | Bilinen proxy CIDR'ları açıkça tanımlanır (spoof edilebilir IP → rate limit atlatma) |
| SQLite tek dosya prod DB | 🟠 Yüksek | PostgreSQL + şifrelenmiş disk |
| Loglar yerel dosyada, PII filtresi yok | 🟡 Orta | Yapılandırılmış log + PII redaksiyonu + merkezî toplama |
| Şifre politikası zayıf (8 karakter, kural yok) | 🟡 Orta | zxcvbn skoru + sızmış şifre kontrolü (HIBP k-anonymity) |
| Test yok → güvenlik regresyonu görünmez | 🟠 Yüksek | Test suite + SAST + dependency scanning CI'da |

---

## 2. Kiracı izolasyonu — katmanlar

```
1. Ağ:        Tenant DB'ler private subnet, sadece Gateway'in SG'sinden erişim
2. Kimlik:    Her proje için ayrı DB kullanıcısı (namines_app_{projectId}), SUPERUSER yok
3. Veritabanı: Proje başına ayrı DB (paylaşımlı şema DEĞİL)
4. Bağlantı:  Ayrı pool, kiracılar arası bağlantı paylaşımı yok
5. Uygulama:  Gateway her istekte projectId'yi token'dan alır, path'ten değil doğrular
6. Satır:     PostgreSQL RLS, SET LOCAL app.claims
7. Kolon:     Gateway'de maskeleme (client-side gizleme yok)
8. Depolama:  S3 prefix izolasyonu + IAM policy
9. Cache:     Redis key prefix + ayrı logical DB
10. Log:      org_id ile etiketli, cross-tenant sorgu imkânsız
```

**Kritik kural:** `projectId` **asla** sadece URL'den alınmaz. Her zaman token'daki claim ile karşılaştırılır.

---

## 3. Sır yönetimi

| Sır | Nerede | Nasıl |
|---|---|---|
| Tenant DB connection string | HashiCorp Vault / AWS Secrets Manager | Zarf şifreleme (KMS), asla control DB'de düz metin |
| BYOK AI anahtarları | Vault | AES-256-GCM (Faz 1 yaklaşımı korunur) + KMS DEK |
| API key'ler | Control DB | Argon2id hash — düz metin **hiç** saklanmaz, oluşturulurken bir kez gösterilir |
| Kullanıcı şifreleri | Control DB | Argon2id (ASP.NET Identity varsayılanı PBKDF2'den yükseltilir) |
| JWT imzalama anahtarı | Vault | Rotasyon destekli (kid header), 90 günde bir |
| Stripe webhook secret | Vault | İmza doğrulaması zorunlu |
| GitHub App private key | Vault | |
| TLS sertifikaları | cert-manager | Otomatik yenileme |

**Sır rotasyonu:** Tüm sırlar için rotasyon prosedürü dokümante ve otomatik. Sızıntı durumunda tek komutla döndürülebilir.

**Kod tarama:** `gitleaks` pre-commit hook + CI'da. Faz 1'de bir telemetri anahtarı repoya sızmış ve sonradan temizlenmişti (`f91352d` commit'i) — bu tekrarlanmamalı.

---

## 4. Girdi güvenliği

| Yüzey | Risk | Önlem |
|---|---|---|
| Gateway sorgu parametreleri | SQL injection | **Sadece parametreli sorgu.** Tablo/kolon adları allowlist'ten (metadata) gelir, kullanıcı girdisinden değil. |
| `/query` ham SQL | Yıkıcı işlem | Ayrı scope, salt-okunur rol varsayılan, SQL parse + statement türü kontrolü, `statement_timeout` |
| NL→SQL | AI'ın tehlikeli SQL üretmesi | Üretilen SQL parse edilir, DDL/DCL reddedilir, kullanıcıya gösterilir, salt-okunur rolle çalışır |
| DDL import | Parser istismarı | Boyut limiti (5 MB), derinlik limiti, timeout |
| URL kazıma | SSRF | `SsrfGuard` (Faz 1 ✔) + redirect kapalı + **DNS rebinding koruması (IP pinleme)** + 500 KB limit |
| Görsel yükleme | Zararlı dosya, zip bomb | Tip doğrulama (magic bytes), boyut limiti, ayrı bucket, `Content-Disposition: attachment` |
| Console dosya alanları | Zararlı yükleme | Antivirüs taraması (ClamAV), presigned URL, kullanıcı içeriği ayrı alan adından servis edilir |
| Prompt | Enjeksiyon | `<untrusted_content>` izolasyonu, yapısal çıktı doğrulaması, AI'ın yan etkisi yok ([09](09-AI-LAYER.md)) |
| Webhook | SSRF (giden) | Hedef URL SSRF kontrolü, sadece public IP, imzalı istek |

---

## 5. Kimlik ve oturum

| Kontrol | Detay |
|---|---|
| Şifre | Argon2id, min 10 karakter, zxcvbn ≥ 3, HIBP kontrolü |
| Brute force | Hesap başına 5 başarısız → 15 dk kilit, IP başına rate limit, CAPTCHA (5. denemeden sonra) |
| Oturum | Access 15 dk / Refresh 30 gün rotasyonlu; tüm oturumları sonlandır butonu |
| Cookie | `HttpOnly; Secure; SameSite=Lax` (aynı site) / `SameSite=None; Secure` (cross-site — Faz 1'deki `Auth__CrossSiteCookie` mantığı korunur) |
| CSRF | SameSite + çift gönderim token'ı (state değiştiren istekler) |
| 2FA | TOTP (Pro+), kurtarma kodları |
| Hassas işlem | Şifre/2FA/faturalama/DB silme → yeniden kimlik doğrulama |
| Bildirim | Yeni cihaz girişi, şifre değişimi, API key oluşturma → e-posta |

---

## 6. Uygulama güvenlik başlıkları

```
Content-Security-Policy: default-src 'self'; script-src 'self' 'wasm-unsafe-eval';
  style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://cdn.namines.com;
  connect-src 'self' https://api.namines.com wss://rt.namines.com;
  frame-ancestors 'none'; base-uri 'self'; form-action 'self'
Strict-Transport-Security: max-age=63072000; includeSubDomains; preload
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: strict-origin-when-cross-origin
Permissions-Policy: geolocation=(), camera=(), microphone=(self)   // mikrofon: sesli giriş için
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Resource-Policy: same-site
```

CORS: allowlist (Faz 1'deki yaklaşım doğruydu ✔), wildcard yok, `AllowCredentials` sadece bilinen origin'lerle.

---

## 7. Altyapı güvenliği

| Katman | Kontrol |
|---|---|
| Container | Non-root kullanıcı, read-only rootfs, `no-new-privileges`, distroless/alpine tabanlı |
| Kubernetes | NetworkPolicy (default deny), PodSecurityStandards `restricted`, RBAC en az yetki |
| Sandbox | gVisor runtimeClass, seccomp, egress deny, kaynak limitleri, TTL |
| Görüntü | Trivy taraması CI'da, imzalı görüntüler (cosign), sadece kendi registry'mizden |
| Bağımlılık | Dependabot + `dotnet list package --vulnerable` + `npm audit` CI'da bloke edici |
| Sırlar | External Secrets Operator, hiçbir sır manifest'te değil |
| Disk | Şifreli volume'lar (at-rest) |
| Ağ | Tüm iç trafik mTLS (service mesh — P2), dış trafik TLS 1.3 |

---

## 8. Veri koruma ve uyumluluk

### KVKK / GDPR

| Gereklilik | Uygulama |
|---|---|
| Veri envanteri | NSL `@tag(pii)` etiketleri → otomatik PII haritası üretimi. **Bu ürünün doğal bir avantajı.** |
| Erişim hakkı | Kullanıcı verisi export API'si |
| Silinme hakkı | Hesap silme → 30 gün grace → tam silme (yedekler dahil, dokümante süre) |
| Veri işleme sözleşmesi | DPA şablonu, alt işleyici listesi (Neon, Groq, Anthropic, Stripe, AWS) |
| Veri ikametgâhı | EU varsayılan, TR opsiyonu (P2) — **Türkiye kurumsal satışta belirleyici** |
| İhlal bildirimi | 72 saat prosedürü, iletişim şablonları |
| Rıza | AI sağlayıcısına veri gönderimi için açık ayar ([09 §9](09-AI-LAYER.md)) |

### AI'a giden veri — net taahhüt

> **Namines hiçbir koşulda müşterinin veritabanı satırlarını LLM sağlayıcısına göndermez.** Sadece şema metadata'sı (tablo/kolon adları, tipler) gönderilir ve bu da `strict` modda anonimleştirilebilir, `local` modda hiç gönderilmez.

Bu, pazarlama sayfasında açıkça yazılmalı — rakiplerin çoğunun net bir cevabı yok.

### SOC 2 hazırlığı (Yıl 2)

Erken yapılması gerekenler (sonradan geriye dönük yapmak pahalı):
- Değiştirilemez audit log (baştan)
- Erişim kontrolü politikaları (dokümante)
- Change management (PR review zorunlu, main'e direkt push kapalı)
- Yedek testi (çeyrekte bir restore tatbikatı)
- Olay müdahale planı
- Çalışan cihaz güvenliği (tek kişilik ekipte bile: disk şifreleme, şifre yöneticisi, 2FA)
- Alt işleyici envanteri

---

## 9. Güvenlik test programı

| Aktivite | Sıklık | Araç |
|---|---|---|
| SAST | Her PR | CodeQL, Semgrep |
| Bağımlılık taraması | Her PR + günlük | Dependabot, Trivy |
| Sır taraması | Her commit | gitleaks |
| Container taraması | Her build | Trivy |
| DAST | Haftalık | OWASP ZAP (staging) |
| Kiracı izolasyon testi | Her PR | Otomatik: kiracı A token'ıyla kiracı B kaynağına erişim denemeleri (100+ senaryo) |
| Sızma testi | Yılda 1 | Harici firma (Yıl 2) |
| Bug bounty | Sürekli | `security.txt` + responsible disclosure (Yıl 2) |

**Kiracı izolasyon test suite'i özellikle önemli** — çok kiracılı bir ürünün en olası felaketi cross-tenant veri sızıntısıdır ve bu regresyon testi olmadan er ya da geç olur.

---

## 10. Olay müdahale

| Seviye | Tanım | Yanıt süresi | Aksiyon |
|---|---|---|---|
| SEV1 | Veri sızıntısı, tam kesinti | 15 dk | Herkes, durum sayfası, müşteri bildirimi |
| SEV2 | Kısmi kesinti, bir kiracı etkilenmiş | 1 saat | Durum sayfası |
| SEV3 | Bozulmuş performans | 4 saat | İç takip |
| SEV4 | Kozmetik | 2 gün | Backlog |

Her SEV1/SEV2 sonrası **suçlamasız post-mortem**, 5 iş günü içinde yayınlanır.

`security@namines.com` + `security.txt` + PGP anahtarı.
