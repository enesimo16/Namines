# 05 — Mobil uygulama: Namines Mobile (iOS + Android)

> **Hedef (ürün sahibi, 2026-10-30):**
> - veritabanını yönetmek,
> - gerektiğinde kapatmak,
> - verilen talimatları işlemek,
> - durumu analiz edip kullanıcının önüne sunmak.
>
> Bunların hepsinde **güvenlik ayağı** olmak: kritik her işlem telefondaki
> donanım anahtarıyla imzalanır. iOS ve Android'de sorunsuz çalışmalı.
>
> Mobilde şema **tasarlanmaz** (tuval dokunmatik ekrana uygun değil, bkz.
> [MOBIL-HEDEFI.md](../MOBIL-HEDEFI.md)). Şema telefondan **yönetilir**.

---

## 1. Teknoloji kararı: iki platform, bir kod tabanı, sıkıntısız

### 1.1 Seçenekler

| | **Expo (React Native)** | **Flutter** | **Kotlin Multiplatform + Compose** | **İki ayrı native** |
|---|---|---|---|---|
| Tek kod tabanı | ✅ | ✅ | ✅ (UI paylaşımı iOS'ta daha genç) | ❌ |
| Dil | TypeScript (bugünkü frontend ile aynı) | Dart (yeni) | Kotlin (yeni) | Swift + Kotlin |
| `@namines/sdk`, i18n, Zod şemaları paylaşımı | ✅ birebir | ❌ yeniden yazılır | ❌ | ❌ |
| Donanım anahtarı / biyometrik imza | Native modül (config plugin) | Plugin | Doğrudan platform API'si | Doğrudan |
| Build/mağaza hattı | EAS Build + Submit (bulutta iOS derler, Mac şart değil) | Kendi CI'ın / Codemagic | Kendi CI'ın | İki ayrı hat |
| OTA güncelleme | EAS Update (yalnızca JS; güvenlik çekirdeği hariç, §5) | Yok (Shorebird ayrı) | Yok | Yok |
| Ekip uyumu (bugün: TS + .NET) | **En yüksek** | Düşük | Düşük | En düşük |

### 1.2 Karar: **Expo**, ama "Expo Go" ile değil

"Expo ile sıkıntı çıkar mı?" sorusunun dürüst cevabı: **eski** Expo (managed + Expo Go, native modül yok) gerçekten sınırlıydı. Bugünkü Expo bu sınırları büyük ölçüde kaldırıyor:

1. **Development build** (`expo-dev-client`): Kendi native modüllerimizi ekleyebildiğimiz özel bir derleme. Expo Go **kullanılmaz**, çünkü Secure Enclave imzası ve passkey gibi modüller orada yok.
2. **Config plugin'ler + CNG (Continuous Native Generation):** `ios/` ve `android/` klasörleri kod deposunda elle bakılmaz; `app.config.ts`'den üretilir. Native sürüm yükseltmesi, en çok sıkıntı çıkaran iş, böylece yönetilebilir hale gelir.
3. **Yeni mimari** (Fabric / TurboModules) varsayılan. Performans sorunları büyük ölçüde geride kaldı.
4. **Gerekirse kaçış:** `expo prebuild` ile tam native projeye geçilebilir. Kilitlenme (lock-in) yok.

**Sıkıntı çıkarabilecek yerler ve önlemleri:**

| Risk | Önlem |
|---|---|
| Kullandığımız 3. parti native kütüphanenin bakımı bırakılması (özellikle biyometrik imza) | Kritik güvenlik modülünü **kendimiz** yazarız: ince bir Swift/Kotlin modülü (Expo Modules API), ~300 satır |
| Expo SDK yükseltmeleri | Yılda 3 SDK sürümü; her yükseltme için ayrı bir iş kalemi + e2e (Maestro) testleri |
| Android cihaz çeşitliliği (StrongBox her cihazda yok) | TEE'ye düş ve güven seviyesini buna göre işaretle (§4) |
| Push bildirim güvenilirliği | Bildirime güvenme; uygulama açılınca bekleyen işler her zaman API'den çekilir |

**Ne zaman Flutter'ı seçerdik?** Ekipte Dart bilen biri olsaydı ve web ile kod paylaşımı önemsiz olsaydı. İkisi de değil.

## 2. Özellikler

### 2.1 Yönetim panosu (v1)

Her bağlı veritabanı için bir kart gösterilir: Ground, Supabase, Neon, BYODB ve masaüstü köprüsü üzerinden yerel DB'ler. Kartta:
- durum (çalışıyor / duraklatıldı / erişilemiyor),
- boyut, bağlantı sayısı, son yedek, kota,
- bekleyen Change Request'ler,
- açık alarmlar,
- "son 24 saatte ne değişti" özeti (şema değişiklikleri, büyük veri değişimleri).

### 2.2 Kontrol eylemleri: "kapat", "durdur", "dondur" (v1)

| Eylem | Nasıl | Sağlayıcı desteği | Onay |
|---|---|---|---|
| **DB'yi duraklat / başlat** | Ground → sağlayıcının suspend/pause API'si | Neon (compute suspend), Supabase (proje pause), Ground local (container stop). Diğerleri desteklemiyorsa düğme görünmez | Biyometri + imza |
| **Salt-okuma moduna al** | Uygulama rolünün yazma yetkisi kaldırılır veya `default_transaction_read_only` açılır (Postgres) | Postgres ailesi | Biyometri + imza |
| **Uzun süren sorguyu öldür** | `pg_terminate_backend` / `KILL QUERY` | Postgres, MySQL | Biyometri |
| **Bağlantıları kes** (bağlantı fırtınası) | Belirli rol veya uygulama için | Postgres, MySQL | Biyometri + imza |
| **API anahtarlarını dondur** | Namines Gateway | Hepsi | Biyometri + imza |
| **Ham SQL'i kapat** (`AllowDeskSql`) | Namines | Hepsi | Biyometri |
| **Otomasyonları durdur** (Flow) | Namines | Hepsi | Biyometri |
| **Otomatik onayı kapat** | Namines | Hepsi | Biyometri |
| **Acil durum: hepsi birden** | Yukarıdakilerin seçili kombinasyonu, tek ekranda | — | Biyometri + imza + 5 sn geri sayım |

- Her eylem **geri alınabilir**; geri alma da aynı onay seviyesini ister.
- Her eylem denetim kaydına kim, ne zaman, hangi cihaz ve hangi imza bilgisiyle yazılır.
- Hiçbir eylem "sessizce başarısız" olmaz: sağlayıcı reddederse kullanıcı ekranda sağlayıcının mesajını görür (Ekim denetimi kuralı).

### 2.3 Talimat işleme: "söyle, planlasın, sen onayla" (v1)

Kullanıcı yazarak veya sesle talimat verir (sesli giriş, Whisper altyapısı var):
- *"orders tablosuna created_at için index ekle"*
- *"dün gece 2'den sonra silinen kullanıcıları göster"*
- *"staging DB'yi hafta sonu duraklat"*
- *"users tablosundaki e-posta sütununu NOT NULL yap"*

**Akış:**
1. **Anlama:** AI talimatı yapılandırılmış bir **plana** çevirir: eylem türü, hedef DB, SQL/DDL, zamanlama. Plan ekranda insan dilinde gösterilir.
2. **Sınıflandırma:**
   - **Okuma** → Gateway `/query/nl` (mevcut) salt-okuma ile çalışır ve sonuç tablo/grafik olarak hemen gösterilir.
   - **Şema değişikliği** → normal **Change Request** açılır; risk analizi, Run Tests ve Neon branch'i varsa gerçek veriyle kanıt yapılır.
   - **Kontrol eylemi** (§2.2) → ilgili eylem planlanır.
3. **Onay:** Risk seviyesine göre biyometri veya biyometri + imza istenir; Destructive için iki kişi kuralı uygulanır (§3).
4. **Uygulama ve sonuç:** Sonuç ve geri alma bağlantısı gösterilir.

**Güvenlik kuralı:** AI **hiçbir zaman** doğrudan çalıştırmaz. AI yalnızca plan üretir; çalıştırma her zaman onaydan geçer. Bu kural, MCP'deki salt-okuma düzeltmesiyle (denetim C-2) aynı ilke.

### 2.4 Analiz ve öneriler: "önüne sunmak" (v1 → v2)

Uygulama kullanıcıya proaktif bir **öneri akışı** sunar. Her kart: ne gördük, neden önemli, tek dokunuşla ne yapılabilir.

| Analiz | Kaynak | Örnek kart |
|---|---|---|
| Eksik index | Mevcut `MissingIndexSuggestion` + sorgu istatistikleri (`pg_stat_statements`, varsa) | "`orders.customer_id` için index yok; en yavaş 3 sorgunun 2'si bunu kullanıyor → CR aç" |
| Güvenlik | Supabase RLS analizi, PII taraması, API anahtarı kullanımı | "`profiles` tablosu `anon`'a açık → RLS'i aç (CR)" |
| Şema sapması (drift) | Canlı ↔ Namines karşılaştırması | "Canlıda Namines'te olmayan 2 sütun var → içe al / geri al" |
| Büyüme ve kota | Boyut trendi | "Bu hızla 9 günde Free kotası dolar" |
| Yedek sağlığı | Vault | "Son 3 yedek başarısız: kimlik doğrulama hatası" |
| Maliyet | Sağlayıcı metrikleri | "staging DB 14 gündür sorgu almadı → duraklat?" |
| Anomali | Gateway audit | "Bu API anahtarı normalin 40 katı satır okudu" |

Haftalık özet bildirimi gönderilir; içeriğe hassas veri konmaz (§5).

### 2.5 Güvenlik ayağı (v1, çekirdek)

#### İmzalı onay

Change Request ve kontrol eylemlerinde kullanılır.
- Telefon, Secure Enclave (iOS) veya StrongBox/TEE (Android) içinde üretilmiş, **dışarı çıkmayan** P-256 anahtarla imzalar. Anahtar biyometriye bağlıdır.
- İmzalanan yük, kullanıcının **gördüğü** değişikliğe bağlıdır:

```json
{ "v":1, "action":"change_request.approve", "crId":"…", "diffSha256":"…",
  "risk":"Destructive", "nonce":"…", "exp":"…", "deviceId":"…" }
```

  - Değişiklik onaydan sonra değişirse karma (hash) tutmaz ve onay geçersiz olur.
  - `nonce` ve `exp` tekrar oynatmayı engeller.
  - İmza denetim kaydında saklanır; onay sonradan inkâr edilemez.

#### Politika (organizasyon ayarı)

| Risk | Varsayılan |
|---|---|
| Safe / Risky | Web, eklenti veya mobil onayı yeterli |
| Breaking | **Mobil imza zorunlu** |
| Destructive | **Mobil imza + iki kişi kuralı** (iki farklı Admin/Owner, iki farklı cihaz) |

#### Alarmlar

Her alarm tek dokunuşta bir eyleme bağlanır:
- yeni cihazdan giriş;
- ham SQL veya export anahtarı oluşturuldu;
- PII'ye toplu erişim;
- yedek hatası;
- şema sapması;
- kota;
- başarısız giriş patlaması.

#### Giriş

- Passkey (platform kasası).
- Web girişinde **numara eşleştirmeli** push onayı (MFA yorgunluğuna karşı).
- Apple ile giriş: iOS'ta zorunlu, [09](09-OAUTH-KATALOGU.md) G4.

## 3. Backend gereksinimleri

1. **Cihaz kaydı + güven seviyesi** ([02](02-ORTAK-TEMEL.md) §3).
   - Yeni cihaz, mevcut güvenilir bir cihazdan ya da passkey + e-posta onayından geçmeden `bound` olamaz.
   - App Attest / Play Integrity ile `attested` seviyesi verilir.
2. **Onay API'si:**
   - `challenge` → nonce;
   - `sign` → ECDSA doğrulama, politika kontrolü, denetim kaydı.
3. **İki kişi kuralı:** `Decide` mantığında "farklı kullanıcı + farklı cihaz" şartı. Mevcut `EffectiveTeamSize` ile uyumlu.
4. **Kontrol eylemleri API'si:**
   - `POST /api/v1/databases/{id}/actions/{pause|resume|read-only|kill-query|terminate-connections}`;
   - her eylem bağlayıcıya delege edilir ([06](06-ENTEGRASYON-MIMARISI.md) yeni yetenek: `Control`);
   - desteklenmeyen eylemler capability listesinde görünmez.
5. **Dondurma durumu (`FreezeState`):** anahtarlar, Desk SQL, Flow, otomatik onay ve deploy için. Denetimde eklenen `FeatureGate` deseni yeniden kullanılır.
6. **Talimat → plan servisi:** AI çıktısı serbest metin değil, şemalı bir **plan nesnesi**dir. Plan doğrulanır; geçersiz plan reddedilir.
7. **Analiz motoru:** `IModuleEventBus` + periyodik görevler → öneri kartları → `NotificationService`.

## 4. Tehdit modeli (özet)

| Tehdit | Önlem |
|---|---|
| Çalınan telefon | Biyometriye bağlı anahtar; uzaktan "cihazı kaldır"; iki kişi kuralı |
| Ele geçirilmiş web veya eklenti oturumu | Breaking/Destructive ve kapatma eylemleri için mobil imza zorunlu |
| AI'ın yanlış veya kötü niyetli plan üretmesi (prompt injection) | AI çalıştırmaz; plan şema doğrulamasından ve risk motorundan geçer; insan imzalar |
| MFA yorgunluğu | Numara eşleştirme, istek sınırı, "ben istemedim" → kilit + alarm |
| Sahte onay ekranı (OTA kanalının ele geçirilmesi) | İmza akışı ve yük gösterimi native tarafta; OTA ile değiştirilemez |
| Root/jailbreak | Attestation başarısız → kritik eylem verilemez (politika) |
| Push'tan sızma | Push yükünde yalnızca `{type, id}` taşınır |
| Ağ dinleme | TLS + HSTS; sertifika sabitleme v2'de (yedek pin + uzaktan kapatma) |

## 5. OTA ve gizlilik kuralları

- **EAS Update** yalnızca görsel ve metin değişiklikleri içindir. İmza yükü kurulumu, doğrulama ekranı ve eylem onayı **mağaza sürümünde** yaşar.
- Analitikte CR içeriği, tablo adı, SQL ve veri tutulmaz. Denetimde eklenen `analyticsScrub` kuralı burada da geçerli.
- Konum, kişiler ve fotoğraf izinleri istenmez. Mikrofon yalnızca sesli talimat için, kullanım anında istenir.

## 6. Test

- **Maestro** e2e (iOS simülatör + Android emülatör), CI'da EAS Build ile.
- **İmza testleri:** değişen CR'ın eski imzası reddediliyor; aynı imza ikinci kez reddediliyor; Destructive tek kişi/tek cihazla geçmiyor; biyometri değişince anahtar geçersiz oluyor.
- **Kontrol eylemleri:** her bağlayıcı için kayıtlı yanıt testi + gece canlı duman testi (Neon suspend/resume).

## 7. Efor ve sıra

| Sürüm | İçerik | Efor (backend dahil) |
|---|---|---|
| v1 | Pano, kontrol eylemleri (pause/resume, salt-okuma, kill, dondurma), talimat → plan → onay, imzalı onay + iki kişi, alarmlar, cihaz yönetimi | 8-10 hafta |
| v1.5 | Passkey girişi, numara eşleştirme, analiz kartları (index, güvenlik, drift, kota) | 3-4 hafta |
| v2 | Maliyet ve anomali analizi, mobil Desk (salt-okuma), sertifika sabitleme, tablet yerleşimi | 4 hafta |

**Bağımlılık:** [02](02-ORTAK-TEMEL.md) tamamen; kontrol eylemleri için [06](06-ENTEGRASYON-MIMARISI.md) `Control` yeteneği.

## 8. Kabul kriterleri

1. Temiz bir iPhone ve bir Android cihazda: kur → passkey ile giriş → cihazı kaydet → bir Neon DB'yi duraklat → yeniden başlat; ikisi de imzalı ve denetim kaydında.
2. "orders tablosuna index ekle" talimatı CR açıyor, risk raporu ve kanıt gösteriyor, imzayla onaylanıyor.
3. AI'ın ürettiği bir DROP planı Destructive olarak sınıflanıyor ve iki kişi kuralı olmadan çalışmıyor.
4. §6'daki imza testlerinin hepsi otomatik olarak geçiyor.
5. Push yükünde içerik yok (paket yakalama ile kanıt).
