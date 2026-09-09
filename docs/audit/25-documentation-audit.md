# 25 — Dokümantasyon denetimi

> ## ✅ DURUM (2026-09-09)
> | Bulgu | Durum |
> |---|---|
> | DOC-001 Üretim çalıştırma kılavuzu yok | ✅ `deploy/URETIM-CALISTIRMA.md` |
> | DOC-002 API dokümantasyonu yayınlanmıyor | 🟡 Açık — P2 |
> | DOC-003 Kullanıcı belgeleri yok | 🟡 Açık — P2 |
> | DOC-004 Belge sayısı fazlalığı | 🟡 Açık — P2 |
> | **DOC-005 `.gitignore` yeni belgeleri sessizce yutuyor** | 🆕 ✅ Bulundu ve düzeltildi |

## Mevcut durum — olağandışı derecede iyi

Bu, denetimin en beklenmedik bulgusu. Çoğu projede dokümantasyon eksik ya da
eskimiş olur; burada **fazla** ve **taze**.

| Belge | Durum | Değerlendirme |
|---|---|---|
| `AGENTS.md` | ✅ Güncel | Oturum başlangıç rehberi, "kesin kurallar" bölümü |
| `DURUM.md` | ✅ Güncel | **Kanıt seviyeleriyle** durum tablosu — ender |
| `README.md` + `README.tr.md` | ✅ İki dilde | — |
| `FRONTEND.md` | ✅ | Palet, tipografi, kütüphane kuralları |
| `new-phase/` (35+ belge) | ✅ | Faz belgeleri, karar kayıtları |
| `second-phase/`, `third-phase/` | ✅ | — |
| `namines-vault/` (3 belge) | ✅ Bugün güncellendi | Genel bakış / plan / erişim-deploy |
| `namines-ground/` | ✅ | v1 kararları |
| `namines_desk/` | ✅ | — |
| `.env.example` | ✅ | **Her anahtarın neden gerektiği yazılı** |
| `docs/audit/` | ✅ | Bu denetim |

### Neyi doğru yapıyor

1. **Kanıt seviyeleri.** `DURUM.md` "yazıldı", "test edildi" ve "canlı
   kanıtlandı"yı ayırıyor. Bu ayrım, bu denetimin de temelini oluşturdu —
   çünkü belgeye güvenilebildi.

2. **Kararların gerekçesi kayıtlı.** "docker.sock ASLA mount edilmez" gibi
   kurallar **nedeniyle** yazılı. Yeni bir geliştirici aynı yanlışı yeniden
   keşfetmek zorunda kalmıyor.

3. **Başarısızlıklar da kayıtlı.** "857 test yeşilken uygulama hiç
   başlamıyordu", "127 integration testi sessizce atlandı" — bu tür kayıtlar
   ender ve en değerli olanlar.

4. **`.env.example`** bir yapılandırma referansı değil, bir **eğitim belgesi**:
   hangi anahtarın hangisinden farklı olması gerektiğini ve neden gerektiğini
   açıklıyor.

---

## DOC-001 — Üretim çalıştırma kılavuzu yok

### Finding
Nasıl **geliştirileceği** ayrıntılı yazılı; nasıl **çalıştırılacağı** değil.

### Eksik olanlar
- [ ] Üretim dağıtım adımları (bkz. DEVOPS-001)
- [ ] Zorunlu ortam değişkenleri **kontrol listesi** (`.env.example` var ama
      "üretimde bunlar olmadan açma" listesi yok)
- [ ] TLS/proxy mimarisi
- [ ] Yedekleme/geri yükleme **çalıştırma** prosedürü (Vault'un kendi belgesi
      var ama control DB'nin yedeği ayrı bir konu)
- [ ] Olay müdahale (incident) prosedürü
- [ ] Geri alma (rollback)

### Risk
Ürün "web'de yayında" ama dağıtım bilgisi tek kişinin belleğinde. Bkz. S-05
(bus factor).

### Severity **HIGH** · ### Effort M · ### Priority P1

### ✅ Yapıldı — `deploy/URETIM-CALISTIRMA.md`
Kapsam: zorunlu ortam değişkenleri (ve neden üçünün birbirinden farklı olması
gerektiği), dağıtım adımları, TLS/proxy gereklilikleri, aynı-site vs cross-site
kararı, **şifreleme anahtarı saklama ve kurtarma prosedürü**, çıkış öncesi
kontrol listesi ve bilinen sınırlar.

Belgedeki en önemli bölüm anahtar kurtarma: `Vault:BackupEncryptionKey`
kaybolursa geçmiş yedeklerin tamamı kalıcı olarak okunamaz hâle gelir ve
kurtarma yolu **tasarım gereği** yoktur. Belge, anahtarın en az iki bağımsız
yerde saklanmasını ve rotation'ın nasıl yapılacağını (eski anahtarı SAKLAYARAK)
yazıyor.

Ayrıca bir sınır **kayda geçti**: Vault çoklu anahtar desteklemiyor, yani
rotation manuel bir işlem. Düzenli rotation gerekiyorsa önce o yetenek
eklenmelidir.

---

## DOC-002 — API dokümantasyonu yayınlanmıyor

Swashbuckle kurulu (`AddSwaggerGen`), yani OpenAPI şeması üretiliyor. Ancak:
- Yayınlanan bir API referansı yok.
- Gateway için `GatewayOpenApiGenerator` var — **kullanıcının** API'si
  belgeleniyor ama **Namines'in kendi** API'si belgelenmiyor.

### Recommendation
Swagger UI'ı en azından kimlik doğrulamalı olarak aç, ya da OpenAPI şemasını
CI'da bir artefakt olarak yayınla.

### Severity **MEDIUM** · ### Effort S · ### Priority P2

---

## DOC-003 — Kullanıcı belgeleri yok

Tüm belgeler **geliştiriciye** yazılmış. Son kullanıcı için:
- [ ] Başlangıç kılavuzu
- [ ] Sorun giderme
- [ ] SSS
- [ ] Terim sözlüğü (Ground / Desk / Gateway / Vault / Eject / NSL)

Terim sözlüğü özellikle gerekli: ürünün kendine ait 10 terimi var (bkz. UX
"Terminoloji değerlendirmesi").

### Severity **MEDIUM** · ### Effort M · ### Priority P2

---

## DOC-004 — Belge sayısı fazlalığı

Kök dizinde 8 markdown, ayrıca `new-phase/` (35+), `second-phase/`,
`third-phase/`, üç modül klasörü. Ayrıca:
- `UI_UX_PRODUCT_AUDIT.md`
- `VERCEL_DESIGN_ADAPTATION.md`
- `Vercel-Inspired UI-UX Analysis & Project Adaptation Task.md`

Son üçü, adlarından anlaşıldığı kadarıyla **bu denetimin öncülleri** ya da
geçmiş görev tanımları. `AGENTS.md` "önce oku" listesi 5 belge sayıyor — yani
gezinme sorunu farkında.

### Problem
Yeni bir geliştirici hangi belgenin güncel olduğunu bilemez. `AGENTS.md`
27-LIFECYCLE-PIVOT.md'nin 24-ROADMAP.md'yi "geçersiz kıldığını" söylüyor —
bu tür geçersizlik ilişkileri belgeler arasında dağınık.

### Recommendation
- Kök dizinde bir `docs/INDEX.md`: hangi belge ne için, hangisi güncel,
  hangisi tarihsel.
- Tarihsel belgeleri `docs/archive/` altına taşı (silme — karar kaydı olarak
  değerliler).
- `Vercel-Inspired ... Task.md` gibi boşluklu ve görev-adı dosyaları temizle.

### Severity **LOW** · ### Effort S · ### Priority P2

---

## Geliştirici onboarding süresi — tahmin

| Aşama | Süre | Dayanak |
|---|---|---|
| Ürünü anlamak | **1 gün** | `new-phase/BASLA-BURADAN.md` (~20 dk) + `DURUM.md` |
| Ortamı kurmak | **2-4 saat** | `docker-compose up` + `.env.example` |
| İlk anlamlı katkı | **2-3 gün** | Kod yorumları gerekçeli, testler kılavuz görevi görüyor |
| Bağımsız çalışabilmek | **1-2 hafta** | 100k satır, 9 proje |

**Karşılaştırma:** Bu ölçekte bir kod tabanında normal süre 3-4 haftadır.
Belgelerin kalitesi bunu yaklaşık **yarıya indiriyor**.

**Ancak:** üretim dağıtımı için onboarding **imkânsız** — belge yok (DOC-001).

---

## Öncelik sırası

1. **DOC-001** üretim çalıştırma kılavuzu — P1, tek kişiye bağımlılığı kırar
2. **DOC-003** kullanıcı belgeleri (terim sözlüğüyle başla) — P2
3. **DOC-004** belge indeksi + arşiv — P2
4. **DOC-002** API referansı — P2


---

## DOC-005 — `.gitignore` yeni belgeleri SESSİZCE yutuyor (YENİ BULGU)

> Bu bulgu, denetim raporlarını commit'lemeye çalışırken ortaya çıktı — yani
> denetimin kendisi tarafından tetiklendi.

### Finding
`.gitignore` bütün markdown'ı yok sayıp bir **allowlist** tutuyor:

```gitignore
*.md
!README.md
!AGENTS.md
!new-phase/*.md
...
```

Yeni bir belge yazdığınızda: dosya oluşur, `git status` onu **hiç göstermez**,
commit atarsınız, belge yalnızca sizin makinenizde kalır. **Hata mesajı yok,
uyarı yok.**

### Location
`.gitignore:141-160`

### Nasıl ortaya çıktı
Bu denetimin 28 raporu ve `deploy/URETIM-CALISTIRMA.md` yazıldı.
`git status --short` hiçbirini listelemedi. Yalnızca şu komutla görüldü:

```bash
git check-ignore -v docs/audit/01-executive-summary.md
# .gitignore:141:*.md    docs/audit/01-executive-summary.md
```

Yani 4.000 satırlık bir denetim, sessizce kaybolmak üzereydi.

### Problem
Bu, denetimin başka bulgularıyla **aynı kalıp**: varsayılanın güvensiz/kaybettiren
tarafta olması ve unutmanın sessiz kalması. `.gitignore`'daki allowlist'e satır
eklemeyi unutmak hiçbir sinyal üretmiyor.

### Risk
Belgelenmiş bir karar, yalnızca onu yazan kişinin diskinde kalır. Bu depo
dokümantasyona olağandışı derecede yatırım yapıyor (bkz. bu raporun başı) —
tam da bu yüzden kayıp burada daha pahalı.

### Severity **MEDIUM**

### ✅ Yapıldı
`!docs/**/*.md` ve `!deploy/*.md` eklendi; ayrıca `.gitignore`'a tuzağı
açıklayan bir uyarı bloğu kondu.

### Kalıcı öneri (P2, S)
`*.md` yerine yalnızca **üretilen** markdown'ları yok sayın
(`**/node_modules/**/*.md` gibi). Varsayılanın "takip et" olması, unutmayı
zararsız hâle getirir — bugün tam tersi.
