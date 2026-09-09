# 21 — Risk kaydı

Olasılık ve etki 1-5. **Şiddet = Olasılık × Etki.**

---

## Kritik riskler (şiddet ≥ 15)

| # | Risk | Olasılık | Etki | Şiddet | Azaltma |
|---|---|---|---|---|---|
| R-01 | **Yanlış `ASPNETCORE_ENVIRONMENT` ile bilinen JWT anahtarı kullanılıyor** → tam kimlik baypası | 4 | 5 | **20** | SEC-005: gate'i `IsDevelopment()`'a çevir + fallback anahtar eşitlik kontrolü. Efor XS. |
| R-02 | **Cross-site cookie ile CSRF** → kullanıcı adına yazma | 4 | 4 | **16** | AUTHZ-004: aynı site altında dağıt, ya da anti-forgery / özel header. Efor S. |
| R-03 | **Denetimsiz keyfi SQL** → veri kaybı, adli inceleme imkânsız | 3 | 5 | **15** | SEC-001: denetim kaydı + risk kapısı + projeye bağlama. Efor M. |
| R-04 | **Paylaşılan şemada saklı XSS** → görüntüleyen kullanıcı adına işlem | 3 | 5 | **15** | SEC-004: `securityLevel: 'strict'`. Efor XS. |

**R-01 ve R-04'ün toplam eforu birkaç saat.** Şiddet/efor oranı bu ikisinde
uçuk yüksek — önce onlar yapılmalı.

---

## Yüksek riskler (şiddet 9-14)

| # | Risk | Olasılık | Etki | Şiddet | Azaltma |
|---|---|---|---|---|---|
| R-05 | **Üretim dağıtımı yeniden kurulamıyor** (tanım depoda yok) | 3 | 4 | 12 | DEVOPS-001: üretim compose/manifest yaz. Efor M. |
| R-06 | **Frontend regresyonu üretimde fark ediliyor** (test yok) | 4 | 3 | 12 | FE-001: store'lardan başlayarak test. Efor L. |
| R-07 | **MSSQL/Oracle FK okuma hatası** (MySQL'de aynı hata bulundu, bu ikisi doğrulanmadı) | 3 | 4 | 12 | I-06: canlı doğrula. Efor M. |
| R-08 | **Sahte jeton ekranı** → kullanıcı yanlış güvenlik varsayımıyla hareket ediyor | 4 | 3 | 12 | SEC-003: kaldır. Efor S. |
| R-09 | **Çalınmış jeton iptal edilemiyor** | 2 | 5 | 10 | AUTH-001: `SecurityStamp` claim'i. Efor M. |
| R-10 | **Kaba kuvvet ile hesap ele geçirme** (lockout yok, parola 8 karakter) | 3 | 4 | 12 | AUTH-002 + SEC-007. Efor S. |
| R-11 | **Erişilebilirlik uyumsuzluğu** → kamu/kurumsal satış engeli | 3 | 3 | 9 | A11Y-001: form etiketleri. Efor M. |

---

## Orta riskler (şiddet 4-8)

| # | Risk | Olasılık | Etki | Şiddet | Azaltma |
|---|---|---|---|---|---|
| R-12 | Executor'da zaman aşımı yok → havuz tükenmesi | 2 | 3 | 6 | REL-001. Efor S. |
| R-13 | MySQL'de DDL transaction yanılsaması → kısmi uygulama | 2 | 4 | 8 | DB-007: kullanıcıya açıkça söyle. Efor S. |
| R-14 | Eşzamanlı düzenlemede sessiz veri kaybı | 2 | 3 | 6 | REL-003a: `RowVersion`. Efor M. |
| R-15 | Migration deploy anında geri alınamaz değişiklik yapıyor | 2 | 4 | 8 | DB-003: ayrı adım. Efor M. |
| R-16 | Docker.DotNet çatışması testlerde patlıyor | 3 | 2 | 6 | TD-005. Efor M. |
| R-17 | DNS rebinding ile iç ağa erişim | 1 | 5 | 5 | SEC-006. Efor M/S. |
| R-18 | Kimliksiz `eject` ile CPU tüketimi | 2 | 3 | 6 | BACK-002: rate limit. Efor XS. |
| R-19 | Bilinen CVE'li bağımlılık (taranmadı) | 3 | 3 | 9 | DEVOPS-004: CI'a tarama ekle. Efor S. |
| R-20 | `pageSize` tavansız (doğrulanmadı) | 2 | 3 | 6 | PERF-007: teyit et. Efor XS. |

---

## Stratejik / ürün riskleri

Bunlar kod riski değil, **iş** riski. Şiddet hesabı aynı ölçekte değil; ayrı tutuluyor.

| # | Risk | Değerlendirme |
|---|---|---|
| S-01 | **Yanlış kategoride konumlanma** | Ürün DBeaver'a göre konumlanırsa "eksik DBeaver" görünür. Bytebase/yönetişim kategorisinde ise güçlü. Bkz. `15`. **En yüksek stratejik risk.** |
| S-02 | **Ground ile altyapı yarışı** | Supabase/Neon ile barındırma yarışı bu ekip boyutunda kazanılamaz. Kaynak orada harcanırsa asıl fark gelişmez. Bkz. RW-02. |
| S-03 | **Özellik genişliği / odak kaybı** | 55 özellik, hiçbiri gereksiz değil ama hepsi birden sürdürülemez. |
| S-04 | **Rakip verisi yok** | Bu denetimde web erişimi çalışmadı; rekabet kararları **veri olmadan** verilemez. Bkz. `16`. |
| S-05 | **Tek kişiye bağımlılık (bus factor)** | Kod ve belgeler tek bir sesle yazılmış. Onboarding belgeleri iyi ama devir planı yok. Bkz. `25`. |

---

## Risk matrisi görünümü

```
Etki
  5 |          R-17        R-09         R-01
  4 |          R-13,R-15   R-05,R-07,   R-02
    |                      R-10
  3 |          R-12,R-14   R-11,R-19    R-06,R-08
    |          R-18,R-20
  2 |                      R-16
  1 |
    +------------------------------------------
        1-2        3            4         Olasılık
```

**Sağ üst köşe = önce buradan başla:** R-01, R-02, R-03, R-04.
