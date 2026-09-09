# 23 — Hızlı kazanımlar

**Tanım:** 1 gün veya daha kısa sürede yapılabilen, etkisi yüksek işler.
Sıra **etki / süre** oranına göre.

---

## 1. Mermaid `securityLevel: 'strict'` — **5 dakika**

**Dosya:** `frontend/components/compile/MermaidPreview.tsx:18`

```diff
- securityLevel: 'loose',
+ securityLevel: 'strict',
```

**Kazanım:** Saklı XSS açığı kapanır (SEC-004, risk şiddeti 15).
**Dikkat:** Tıklanabilir düğüm kullanılıyorsa bozulur — önce `/compile` sayfasını
aç ve diyagramın hâlâ doğru çizildiğini gör.

---

## 2. JWT fallback anahtarını daralt — **15 dakika**

**Dosya:** `backend/Namines.API/Program.cs:164-173`

```diff
- if (builder.Environment.IsProduction())
+ if (!builder.Environment.IsDevelopment())
      throw new InvalidOperationException("Jwt:Key ... zorunludur");
```

Ek olarak, anahtar verilmiş olsa bile fallback ile aynıysa fırlat.

**Kazanım:** Risk şiddeti 20 olan **en yüksek risk** kapanır (SEC-005).
**Test:** `ASPNETCORE_ENVIRONMENT=Staging` ile aç → fırlatmalı.

---

## 3. Sahte jeton ekranını kaldır — **1 saat**

**Dosya:** `frontend/components/canvas/panels/AIPreferencesModal.tsx:471-497`
ve ilgili UI bloğu.

**Kazanım:** Yanlış güvenlik sözü ortadan kalkar (SEC-003), 1.521 satırlık
dosya küçülür, ölü kod gider.

---

## 4. `CompileController`'a rate limit — **10 dakika**

**Dosya:** `backend/Namines.API/Controllers/CompileController.cs:27`

```diff
+ [EnableRateLimiting("sensitive")]
  [ApiController]
  [Route("api/[controller]")]
  public class CompileController : ControllerBase
```

**Kazanım:** Kimliksiz `eject` ile CPU tüketimi sınırlanır (BACK-002).

---

## 5. CI'a bağımlılık güvenlik taraması — **30 dakika**

**Dosya:** `.github/workflows/ci.yml`

```yaml
- name: Bağımlılık güvenlik taraması (backend)
  run: dotnet list backend/Namines.sln package --vulnerable --include-transitive

- name: Bağımlılık güvenlik taraması (frontend)
  run: npm audit --audit-level=high
  working-directory: frontend
```

**Kazanım:** Bilinen CVE'ler görünür olur (R-19). Bu denetim taramayı
yapamadı — **bilinmeyen bir risk** kapanır.

---

## 6. CI'a atlanan test eşiği — **30 dakika**

`.trx` çıktısındaki `Skipped` sayısı 5'i aşarsa build kırılsın.

**Kazanım:** `DURUM.md`'de kayıtlı "127 test sessizce atlandı, suite yine
Başarılı dedi" felaketi bir daha yaşanmaz (TEST-001).

---

## 7. Desk'e boş durumlar — **4 saat**

**Dosyalar:** `services/desk/app/{Projects,Vault,Ground,Members,Logs,Analytics,Deployments}.tsx`

Her ekrana: ne olduğu + sonraki adım + o adıma giden düğme.

**Kazanım:** Yeni kullanıcı terk oranı. Bu listedeki **ürün etkisi en yüksek**
madde (UX-001, F-05).

---

## 8. `ClassifyConnectionFailure`'ı ortak servise taşı — **2 saat**

`GatewayKeyController`'daki private metodu bir yardımcıya çıkar, executor'da kullan.

**Kazanım:** SEC-002 kapanır; aynı sorunun iki farklı cevabı olması biter.

---

## 9. Executor'a komut zaman aşımı — **30 dakika**

**Dosya:** `backend/Namines.Infrastructure/Services/DatabaseExecutorService.cs`

```diff
  command.CommandType = CommandType.Text;
+ command.CommandTimeout = 60;
```

**Kazanım:** Havuz tükenmesi riski azalır (REL-001).

---

## 10. `pageSize` tavanını teyit et (ve yoksa koy) — **30 dakika**

`GatewayController`'da `pageSize` doğrulamasını ara. Yoksa `Math.Min(pageSize, 200)`.

**Kazanım:** Bilinmeyen bir risk kapanır (PERF-007) ya da "zaten vardı" diye
kayda geçer — ikisi de değerli.

---

## Toplam

| | |
|---|---|
| Süre | **~1,5 gün** |
| Kapanan kritik risk | 2 (R-01, R-04) |
| Kapanan yüksek risk | 2 (R-08, R-19) |
| Kapanan orta risk | 4 |
| Ürün etkisi | Boş durumlar → dönüşüm |

**Bu liste, projedeki en verimli 1,5 gün.** Hiçbiri mimari değişiklik
gerektirmiyor, hiçbiri geri alınamaz değil.
