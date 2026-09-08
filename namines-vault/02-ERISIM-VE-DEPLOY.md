# Namines Vault — Erişim modeli ve deploy

Bu belge iki soruyu cevaplıyor:

1. **Vault'a nereden erişiyoruz?** Kendi sitesi mi var, Desk'in içinde mi?
2. **Proje deploy edildiğinde bunlar otomatik olarak çalışır mı?**

---

## 1. Vault'un kendi sitesi YOK

**Vault bir arayüz uygulaması değil, bir arka uç modülü.** Kullanıcının gördüğü
ekran **Namines Desk'in içinde bir görünüm** — soldaki *İşlemler* grubunda
"Yedekler".

```
Tarayıcı
   │
   └─ Namines Desk  (Next.js, :3200)     ← kullanıcının gördüğü TEK arayüz
         │  services/desk/app/Vault.tsx
         │  services/desk/lib/vault.ts
         │
         └─ HTTP ─→ Namines.API  (:5000)
                       │  Controllers/VaultController.cs   → /api/vault/*
                       │  Infrastructure/Services/VaultService.cs
                       │
                       └─ Namines.Vault  (sınıf kütüphanesi, sunucu yok)
                             ├─ pg_dump / pg_restore  → geçici konteyner
                             └─ AES-256-GCM + dosya deposu
```

### Neden ayrı bir site değil

Üç somut sebep:

**Yedekleme veriye erişimin bir parçası, ayrı bir ürün değil.** Kullanıcı zaten
Desk'te verisine bakıyor, satır düzenliyor, SQL çalıştırıyor. "Bu verinin bir
kopyasını al" o bağlamın içinde anlamlı; ayrı bir siteye gönderilmek, projeyi
ve oturumu ikinci kez seçtirmek demek olurdu.

**İkinci bir arayüz, ikinci bir yetki yüzeyi demek.** Vault'un yaptığı en yıkıcı
işlem (geri yükleme) veritabanının tamamını siliyor. Bunu Desk'in zaten
doğrulanmış oturumu ve rol modeli (`OrgAccess`) üzerinden yapmak, aynı kararı
ikinci bir yerde tekrar uygulamaktan hem daha güvenli hem daha az kod.

**`Namines.Vault` yine de ayrı bir proje** — ama derleme sınırı olarak, ürün
sınırı olarak değil. `Namines.Vault.csproj` yalnızca `Namines.Core`'a bağlı;
`Namines.Infrastructure` referansı **yok**. Yedekleme mantığının veritabanına
ya da kimliğe bulaşması derleme hatası veriyor.

### Erişim özeti

| Katman | Nerede çalışır | Kullanıcı görür mü |
|---|---|---|
| `Namines.Vault` | `Namines.API` sürecinin içinde, kütüphane olarak | Hayır |
| `/api/vault/*` | `Namines.API` (:5000) | Hayır (Desk çağırır) |
| "Yedekler" ekranı | Namines Desk (:3200) | **Evet — tek giriş noktası** |

Ana Namines uygulamasında (`frontend/`, :3000) Vault ekranı **yok**. Yedekleme
canlı veritabanı bağlanmış projeler için anlamlı; canlı bağlantı Desk'in
konusu, tasarım düzleminin değil.

### Yetkiler

| İşlem | Gereken rol | Neden |
|---|---|---|
| Yedekleri listeleme | Üye (Viewer+) | Salt okuma |
| Yedek alma | Editor+ | Veriyi değiştirmez, kaybı önler — dar bir role kilitlemek riski artırırdı |
| Doğrulama | Editor+ | Geçici bir sunucuda yapılır, projenin veritabanına dokunmaz |
| İndirme / silme | Admin+ | İndirme, verinin TAMAMINI almaktır |
| Zamanlama ayarı | Admin+ | Diski ve yedek geçmişini kalıcı etkiler |
| **Geri yükleme** | **Owner** | Veritabanının üzerine yazar; faturalama/org silme ile aynı ağırlıkta |

---

## 2. Deploy edildiğinde ne otomatik olur, ne olmaz

Kısa cevap: **zamanlanmış yedekler otomatik çalışır, ama üç şart sağlanmadan
çalışmaz — ve ikisi bugünkü `docker-compose.yml`'de sağlanmıyor.**

### Kendiliğinden olan

| Şey | Neden otomatik |
|---|---|
| Veritabanı tabloları (`VaultBackups`, `VaultRestores`, `VaultSchedules`) | EF migration'ları açılışta uygulanıyor — elle SQL yok |
| Zamanlayıcının başlaması | `AddHostedService<VaultScheduleBackgroundService>()`; API her açıldığında başlar |
| Zamanlanmış yedeğin doğrulanması | Her otomatik yedekten sonra kendiliğinden denenir |
| Eski otomatik yedeklerin temizlenmesi | Saklama sayısı aşıldığında |
| Çoklu instance'ta tek yedek | Koşullu `UPDATE` ile satır kapma — instance sayısından bağımsız |

### Sağlanması GEREKEN üç şart

**1. `Vault__BackupEncryptionKey` tanımlı olmalı.**
Tanımsızsa Vault **açıkça durur** — şifresiz yedek yazmaz. Bu bilinçli:
sessizce düz metin yedek almak, şifrelemenin hiç olmamasından daha kötü olurdu,
çünkü kullanıcı korunduğunu sanır.

> `Security__ConnectionEncryptionKey` ile **aynı olmamalı**. Aynı anahtar,
> birinin sızmasını ikisinin sızması yapar: bağlantı dizesini çözebilen biri,
> o bağlantının bütün geçmiş yedeklerini de çözebilirdi.

**2. API'nin Docker daemon'una erişimi olmalı.**
`pg_dump`/`pg_restore` geçici bir `postgres:17-alpine` konteynerinde çalışıyor.
Bugünkü `docker-compose.yml`'de backend konteynerinin daemon'a erişimi **yok**
(`docker.sock` bilinçli olarak mount edilmiyor — host'ta root eşdeğeri yetki
verirdi). Bu hâliyle **deploy'da yedekleme çalışmaz.**

Seçenekler, tercih sırasıyla:

- **API'yi host üzerinde çalıştırmak** (konteyner içinde değil). Daemon'a erişim
  doğal olarak var. En az değişiklik.
- **Provisioning broker'ı** — `new-phase/06-DATA-PLANE.md`'de zaten planlanmış
  olan, konteyner işlerini yapan ayrı ve dar yetkili servis. Doğru uzun vadeli
  cevap; `docker.sock`'u kimseye vermeden aynı işi yapar.
- **`docker.sock`'u backend'e mount etmek** — çalışır ama **önerilmez**:
  AGENTS.md'nin açık kuralına aykırı ve backend'i ele geçiren biri host'un
  tamamını ele geçirir.

**3. Yedeklerin durduğu yer KALICI olmalı.**
v1 sunucu diskini kullanıyor (`Vault__StoragePath`). Konteyner içinde varsayılan
yol geçicidir: **konteyner yeniden yaratıldığında bütün yedekler gider.** Bu,
yedeklemenin tam da işe yaraması gereken anda yok olması demek.

Bugünkü compose'da `namines-app-data` adında bir volume zaten var; `Vault__StoragePath`
onun altında bir yola verilmeli. Kalıcı çözüm nesne depo (S3/MinIO) — `IBackupStore`
arkasında ikinci uygulama olarak gelecek; sağlayıcı seçimi hâlâ açık karar.

### Deploy kontrol listesi

```bash
# 1) Yedek şifreleme anahtarı — baglanti anahtarindan FARKLI
Vault__BackupEncryptionKey=$(openssl rand -base64 32)

# 2) Yedekler kalıcı bir volume'de dursun
Vault__StoragePath=/data/vault-backups

# 3) pg_dump imaj etiketi sunucu sürümüne eşit ya da ondan YENİ olmalı
#    (eski bir pg_dump yeni bir sunucuyu reddeder — ve bu iyi:
#     sessizce eksik dump almaktan iyidir)
Vault__PostgresImage=postgres:17-alpine
```

Ayrıca: API'nin Docker daemon'una erişimi (yukarıdaki 2. şart).

### Doğrulama

Deploy'dan sonra tek komutla:

```bash
curl -s https://<api>/api/vault/health
```

Dönen `store` alanı yedeklerin **nerede** durduğunu söyler. Geçici bir yol
görüyorsanız 3. şart sağlanmamış demektir.

---

## Bilinen sınırlar (v1)

| Sınır | Durum |
|---|---|
| Yalnızca PostgreSQL | Bilinçli. Yazılıp canlı doğrulanmamış bir motor "destekleniyor" sayılmaz. MySQL v6'da. |
| Yedekler sunucu diskinde | `IBackupStore` arkasında; S3/MinIO ikinci uygulama olarak gelecek |
| Kısmi geri yükleme yok | Tek tablo değil, veritabanının tamamı geri yüklenir |
| Kaçırılan çalıştırma telafi edilmez | Bilinçli: sunucu iki gün kapalı kaldıysa açılışta iki yedek almak diski doldurmaktan başka işe yaramaz |
| Yedekleme sırasında ilerleme göstergesi yok | İstek, yedek bitene kadar açık kalır. Büyük veritabanlarında arka plan işine taşınmalı. |
