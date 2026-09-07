# Namines Ground — Genel Bakış (plan)

> **Durum:** plan, henüz kod yok. Sıra: [`third-phase/00-BASLA-BURADAN.md`](../third-phase/00-BASLA-BURADAN.md)
> §3'e göre **Desk → Vault → Ground** — Ground **en son**, çünkü en büyük efor
> ve en yüksek risk odur (aşağıdaki §2). Bu doküman, Vault planlanıp bitmeden
> **başlanmayacağını** varsayar; burada yazılanlar sıradaki adımlar için
> referans, bugünün işi değil.

---

## 1. Ground nedir, ne değildir

**Namines Ground**: Namines projelerine **yönetilen, kalıcı, canlı** bir
veritabanı sağlayan bir yetenek — Supabase/Neon'un kendi sağladığı
"veritabanını biz barındırırız" hizmetinin Namines'e entegre edilmiş hâli.
Ana backend'in **içinde** bir .NET modülü olarak (`Namines.Ground.*` isim
alanı), Vault'la (bkz. `namines-vault/00-GENEL-BAKIS.md` §1) **aynı
gerekçeyle** ayrı bir deploy birimi DEĞİL.

> **Karar değişikliği (2026-09-07):** Vault'takiyle birebir aynı gerekçe.
> Ground'un provizyon akışı `CloudProject.EncryptedConnectionString`'i
> **yazıyor** — yani ana backend'in ZATEN sahip olduğu şifreleme
> bileşenine ve veri modeline muhtaç. Ayrı bir servis olsaydı bu ya
> kopyalanır ya da her provizyon/silme işleminde ana API'ye geri HTTP
> çağrısı atılırdı; ikisi de kazancı olmayan bir maliyet. Üstelik Ground'un
> kendi kullanıcı arayüzü de yok — Desk'in "Projeler" panosundan
> tetikleniyor (§1'in kendi tanımı). third-phase §5'in dayandığı
> `second-phase/14-AYRI-URUN-DEVELOPMENT-HOSTING.md` da zaten farklı bir
> konu hakkında (kullanıcının kendi uygulamasını barındırma riski) —
> Ground'a hiç uygulanmıyor.
>
> **İzolasyon kod seviyesinde korunuyor:** kendi isim alanı
> (`Namines.Ground.*`), kendi klasörü (`backend/Namines.Ground/`), kendi
> rota öneki (`/api/ground/*`), kendi arayüzü (`IDatabaseProvider`, §4.1).
> Arayüzde hâlâ **"Namines Ground"** olarak adlandırılıyor.

**Ground DEĞİLDİR:**
- **Kullanıcının uygulamasını barındırma.** `second-phase/14`'ün barındırma
  reddi (7/24 nöbet, kötüye kullanım, yasal sorumluluk, boşta yanan kaynak)
  burada da geçerli — Ground yalnızca **veritabanını** barındırır, üstüne
  yazılan uygulamayı değil.
- **Bugün zaten var olan ephemeral/branch DB sistemi.** Data plane'de
  (`new-phase/06-DATA-PLANE.md`) branch başına gerçek bir yerel PostgreSQL
  zaten var (TTL'li, rastgele parolalı, `docker.sock` mount edilmeden) — bu
  **PR önizleme/deneme** amaçlı, kalıcı değil. Ground bunun **kalıcı,
  production** karşılığı; ikisi aynı şey değil, ikisi de var olacak.
- **Kendi işlettiğimiz bir PostgreSQL cluster'ı.** Aşağıdaki §2 bunun
  bilinçli olarak neden v1'de tercih edilmediğini anlatıyor.

---

## 2. Dürüst risk değerlendirmesi (third-phase'in kendi uyarısı)

> third-phase/00-BASLA-BURADAN.md §3'ten aynen: *"Ground hakkında dürüst
> uyarı: second-phase/14'te barındırmayı reddetme gerekçelerinin (7/24
> nöbet, kötüye kullanım, yasal sorumluluk, boşta yanan kaynak) tamamı
> yönetilen veritabanı için de geçerli — üstelik daha ağır, çünkü veri
> kaybı ürünü bitirir."*

Bu risk, Ground'un en son sırada olmasının TEK sebebi. Azaltma stratejisi:
**kendi Postgres'imizi işletmek yerine ilk sürümde Neon'u arkada sağlayıcı
olarak kullanıp üstüne yalnızca kota/izolasyon/kullanıcı deneyimi katmanı
yazmak** — aynı ürünü, 7/24 nöbet riski olmadan verir. Neon zaten
`new-phase/06-DATA-PLANE.md` §3.1'de "birincil sağlayıcı" olarak seçilmiş
(gerekçe: gerçek copy-on-write branching, scale-to-zero, API ile
provisioning — bunları sıfırdan yazmak aylar sürer).

**Neon hesabı:** [`34-SENDEN-BEKLENENLER.md`](../new-phase/34-SENDEN-BEKLENENLER.md)'de
zaten 🟢 (onaylı/hazır) olarak işaretli — Ground'un önündeki hesap engeli
şimdiden kalkmış durumda.

---

## 3. Kapsam — v1 (MVP, third-phase'in "en büyük efor" uyarısına rağmen küçültülmüş)

`new-phase/06-DATA-PLANE.md`'deki tam vizyon (Kubernetes Job API, gVisor,
NATS, PgBouncer, sıcak havuz, Data Factory) **v3+ için** — bu, third-phase'in
kurduğu "önce yürüyen iskelet" disipliniyle çelişir. v1, o vizyonun yalnızca
Neon-destekli, tek-motor, tek-bölge alt kümesi:

| # | Yetenek | v1'de var mı |
|---|---|---|
| 1 | Proje başına yönetilen PostgreSQL provizyonu (Neon API) | ✔ |
| 2 | Bağlantı bilgisi Desk/Vault ile AYNI şifreleme mekanizmasıyla saklanır | ✔ |
| 3 | Plan bazlı kota (depolama, bağlantı sayısı) | ✔ — sabit, basit sayılar |
| 4 | Veritabanı silme (proje silindiğinde) | ✔ |
| 5 | Neon copy-on-write branch (PR/deneme önizlemesi) | ❌ v2 — data plane'in kendi ephemeral sistemi bunu KISMEN zaten karşılıyor |
| 6 | Kendi bölgesini seçme (multi-region) | ❌ v2 |
| 7 | MySQL/SQL Server yönetilen DB | ❌ v2 — Neon yalnızca PostgreSQL; başka motor için başka sağlayıcı (PlanetScale/Azure SQL) ayrı entegrasyon ister |
| 8 | Kendi Postgres cluster'ını işletmek (Neon'suz) | ❌ kalıcı olarak ertelendi — bkz. §2 |

---

## 4. Mimari

### 4.1 Sağlayıcı soyutlaması

`new-phase/06-DATA-PLANE.md` §3.1'in zaten tanımladığı arayüz yeniden
kullanılır (icat edilmez, referans alınır — `Namines.Ground` isim alanı
altında, modül sınırı gereği kendi arayüz tanımı):

```csharp
public interface IDatabaseProvider {
    Task<ProvisionedDatabase> CreateAsync(ProvisionSpec spec, CancellationToken ct);
    Task DeleteAsync(string databaseId, CancellationToken ct);
    Task<DatabaseMetrics> GetMetricsAsync(string databaseId, CancellationToken ct);
    ProviderCapabilities Capabilities { get; }
}
```

v1'de tek uygulama: `NeonProvider`. Arayüz baştan var olsun ki ikinci
sağlayıcı (`PlanetScaleProvider`, `AzureSqlProvider`) eklenirken Ground'un
geri kalanı değişmesin.

### 4.2 Akış (v1)

```
Kullanıcı (Ground arayüzü ya da ana Namines'in "Projeyi barındır" düğmesi)
  → POST /api/ground/provision/{projectId}
      → Yetki: proje Owner'ı (Desk'in SQL konsolu eşiğiyle aynı — barındırılan
        bir kaynak açmak, en yüksek yetki seviyesini gerektirir)
      → Kota kontrolü (plan bazlı: kaç yönetilen DB, hangi boyut sınıfı)
      → NeonProvider.CreateAsync → gerçek bir Neon projesi/dalı oluşturulur
      → Bağlantı dizesi, Desk/Vault'un KULLANDIĞI AYNI
        AesGcmConnectionSecretProtector mekanizmasıyla şifrelenip
        CloudProject'e (ya da Ground'un kendi eşdeğer kaydına) yazılır
      → Kullanıcıya: "Bağlan" düğmesi — bağlantı dizesi TARAYICIYA HİÇ GİTMEZ,
        Desk'in D1 kararıyla birebir aynı ilke
  → Silme: proje silindiğinde NeonProvider.DeleteAsync tetiklenir — burada
    "bekleyen silme" (soft-delete + N gün sonra kalıcı silme) ZORUNLU,
    çünkü yanlışlıkla tetiklenen bir silme geri alınamaz veri kaybı demek
```

### 4.3 Kota ve izolasyon

- Her proje kendi Neon branch'i/veritabanı — kiracılar arasında **veritabanı
  seviyesinde** izolasyon (aynı sunucu paylaşımı değil).
- Plan bazlı sınırlar: depolama (GB), eşzamanlı bağlantı, aylık compute
  saati (Neon'un kendi faturalama biriminden türetilir — Namines kendi
  saymaz, Neon'un API'sinden okur; **uydurma sayı yok** ilkesi burada da
  geçerli).
- **Scale-to-zero** Neon'un kendi özelliği — Free tier'ın ekonomisi tam
  olarak buna dayanıyor (`06-DATA-PLANE.md`'nin kendi notu).

---

## 5. Güvenlik

Desk ve Vault'ta kurulan üç kalıcı ilke Ground'da da AYNEN geçerli:

1. **Bağlantı dizesi tarayıcıya asla gitmez** — sunucu tarafında şifreli
   durur, JWT + proje yetkisiyle çözülür (Desk D1).
2. **Silme geri dönüşsüz bir eşik gerektirir** — Vault'un restore onayına
   benzer, "proje adını yaz" düzeyinde bir onay (§4.2).
3. **Her provizyon/silme audit trail'e yazılır** — `GatewayAuditEntry`
   deseninin Ground'daki karşılığı, `GroundProvisionEntry`.

Ek olarak Ground'a özgü:

4. **Neon API anahtarı yalnızca sunucuda** — hiçbir istemci, hiçbir
   koşulda Neon'un kendi API anahtarını görmez; Ground bir vekil (proxy)
   olarak davranır.
5. **Kiracı başına ayrı kimlik bilgisi** — `namines_app_{projectId}` deseni
   (`06-DATA-PLANE.md` §3.3), `SUPERUSER` değil.

---

## 6. Ground'un Vault'a bağımlılığı

Ground'un yönettiği veritabanları için **otomatik yedekleme** Vault'un işi
olacak — Ground kendi yedek mekanizmasını YENİDEN YAZMAZ. Bu yüzden sıra
bilinçli: Vault önce bitmeli ki Ground'un "yönetilen DB'niz otomatik
yedekleniyor" vaadi gerçek bir mekanizmaya dayansın, boş bir söz olmasın.

(Neon'un kendi PITR'ı zaten var — Ground v1, Neon'un point-in-time
restore'unu kullanıcıya olduğu gibi yansıtabilir; Vault'un üstüne inşa
edeceği şey **Vault'un desteklediği tüm motorlar için tutarlı bir yedek
deneyimi**, yalnızca Neon'a özgü olmayan.)

---

## 7. Kalıcı olarak Ground'a GİRMEYECEKLER

- **Kendi işlettiğimiz veritabanı sunucusu** (Kubernetes + CloudNativePG) —
  `06-DATA-PLANE.md`'nin kendi notu: "v3, ölçek ekonomisi anlamlı olunca."
  Bugünkü ölçekte bu, nöbet yükünü karşılıksız üstlenmek olurdu.
- **Kullanıcının uygulamasını barındırma** — yalnızca veritabanı, asla
  uygulama kodu (§1'in tekrarı, çünkü en sık karışan nokta bu).
- **Çok-bölgeli, çok-sağlayıcılı otomatik failover orkestrasyonu** — Neon'un
  kendi SLA'sına güvenilir; bunu tekrar icat etmek v1 kapsamı dışı.

---

## 8. Bu planın açtığı yeni "senden beklenenler"

`new-phase/34-SENDEN-BEKLENENLER.md`'ye eklenmesi gereken açık kararlar:

1. **Plan bazlı Ground kotaları** — hangi plan kaç yönetilen veritabanı,
   hangi boyut sınıfı (nano/small/medium) alır? (`06-DATA-PLANE.md` §3.2'nin
   taslak spesifikasyonu bir başlangıç noktası, onay bekliyor.)
2. **Silme bekleme süresi** — "proje silindi" ile "Neon veritabanı kalıcı
   olarak silindi" arasında kaç gün tampon olacak?
3. **Neon organizasyon/proje yapısı** — her Namines projesi kendi Neon
   projesi mi olacak, yoksa tek bir Neon organizasyonu altında branch'ler
   mi? (Faturalama ve izolasyon üzerinde doğrudan etkisi var.)

---

## 9. Değişmeyen kurallar (third-phase ve Desk/Vault'tan miras)

1. **Modül sınırı.** Ayrı bir deploy birimi değil, ama `Namines.Ground.*`
   isim alanı dışına sızmayan, kendi arayüzü (`IDatabaseProvider`) üzerinden
   çağrılan bir iç disiplin — third-phase §5'in ilkesinin modül karşılığı
   (§1).
2. **Kanıt = çalışan komut + görülen çıktı.** Bir Neon veritabanının
   gerçekten oluştuğu, gerçek bir bağlantıyla (Vault'un §1'deki "bağımsız
   psql doğrulaması" ilkesiyle aynı) kanıtlanır — API'nin "başarılı" demesi
   yetmez.
2b. **Dürüst risk iletişimi.** §2'deki uyarı, kod yazılırken de, kullanıcıya
   sunulan her metinde de korunur — Ground'un veri kaybı riski hiçbir yerde
   küçük gösterilmez.
3. **Yürüyen iskelet önce.** Tek sağlayıcı (Neon), tek motor (PostgreSQL),
   tek akış (provizyon + silme) uçtan uca kanıtlanmadan branch/multi-region/
   ikinci sağlayıcı eklenmez.
4. **Uydurma sayı yok.** Kota ve kullanım metrikleri Neon'un kendi API'sinden
   okunur, tahmin edilmez.

---

## 10. Ek özellikler (öneri, sınır içinde)

Aşağıdakiler Ground'un **tek işine** (bir projeye yönetilen veritabanı
sağlamak/silmek) hizmet ediyor — Vault'un yedekleme işine ya da Desk'in
veri yönetimine taşmıyor.

1. **ChangeRequest'e bağlı otomatik önizleme veritabanı.** Bir şema
   değişikliği incelemeye açıldığında (Desk'in "Sürümler" ekranındaki
   `ChangeRequest`), Ground otomatik olarak Neon'un copy-on-write branch
   özelliğiyle GERÇEK VERİYE SAHİP bir önizleme veritabanı açar — migration
   gerçek veri şekliyle denenebilir. CR kapanınca (onaylanır/reddedilir)
   branch otomatik silinir. Bu, Ground'un provizyon yeteneğini var olan
   inceleme akışına bağlıyor — yeni bir yetenek değil, mevcut ikisinin
   kesişimi.
2. **Kullanım panosu.** Neon'un kendi API'sinden okunan depolama/compute
   kullanımı, Desk'in Projeler panosunda gösterilir (uydurma sayı yok —
   §9 madde 4'ün doğal uzantısı).
3. **Sağlık göstergesi.** Neon'a erişilebilirlik + gecikme — basit bir
   healthcheck, Desk'in üst şeridinde küçük bir nokta/rozet olarak.
4. **"Son silinenler" listesi.** §4.2'nin "bekleyen silme" penceresi
   içindeki projeler ayrı bir listede — kullanıcı yanlışlıkla sildiği
   projeyi pencere kapanmadan geri getirebilir.
5. **Bölge seçimi (v2).** Kullanıcının coğrafi yakınlığına göre Neon
   bölgesi seçmesi — §3'te zaten v2 olarak işaretli, burada tekrar
   vurgulanıyor çünkü gecikme şikayetlerinin en olası kaynağı bu.
