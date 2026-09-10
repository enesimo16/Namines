# Migration kuralı ve geri alma prosedürü

**Son güncelleme:** 10.09.2026 · Kaynak bulgu: `DEVOPS-005`
(bkz. [10-devops-deployment-audit.md](../docs/audit/10-devops-deployment-audit.md))

---

## 1. Neden bir kural gerekiyor

Geri alma (rollback), kodu eski sürüme döndürmektir. **Veritabanını
döndürmez.** Sonuç şu: geri alındıktan sonra **eski kod, yeni şemaya karşı
çalışmak zorunda kalır.**

Bu, iki şeyden birinin doğru olmasını gerektirir:

- Ya her migration eski kodla da çalışabilir hâlde yazılır,
- ya da geri alma diye bir şey yoktur — yalnızca "ileri doğru düzeltme" vardır
  ve bozuk bir dağıtım, düzeltme yazılana kadar üretimde kalır.

Namines'te migration'lar açılışta ya da `--migrate` ile koşuyor
(`Program.cs`, `Database:MigrateOnStartup`). Yani ilk seçeneği zorunlu kılan
bir kurala ihtiyaç var.

---

## 2. KURAL: her migration ileriye uyumlu olmalı

> **Bir migration, o migration'dan ÖNCEKİ sürümün kodu tarafından
> çalıştırıldığında da uygulamayı bozmamalı.**

Somut karşılığı: **tek bir dağıtımda hem yeni hem eski şekli aynı anda
desteklenir**, eski şekil ancak *bir sonraki* dağıtımda silinir.

### İzin verilenler (tek adımda güvenli)

| İşlem | Neden güvenli |
|---|---|
| **Nullable** kolon ekle | Eski kod kolonu bilmez, `INSERT`'lerinde yer vermez, veritabanı `NULL` yazar |
| Varsayılanı olan kolon ekle | Aynı — eski kodun `INSERT`'i çalışmaya devam eder |
| Yeni tablo ekle | Eski kod ona hiç dokunmaz |
| Yeni index / unique olmayan kısıt ekle | Okuma-yazma sözleşmesi değişmez |
| Yeni enum değeri **sona** ekle | Sayısal karşılıklar kaymaz (bkz. `GatewayWriteKind.Read` yorumu) |

### YASAK olanlar (tek adımda)

| İşlem | Ne kırar |
|---|---|
| Kolon **sil** | Eski kod hâlâ `SELECT`'liyor → sorgu patlar |
| Kolon **yeniden adlandır** | Hem eski hem yeni kod için yarısı kayıp |
| `NOT NULL` yap (varsayılan olmadan) | Eski kodun o kolonu vermeyen `INSERT`'i reddedilir |
| Tip **daralt** (`text` → `varchar(50)`) | Eski kodun yazdığı uzun değer reddedilir |
| Unique kısıt ekle | Eski kodun yazdığı çift kayıt reddedilir |
| Enum değerini **araya** ekle / sırasını değiştir | Kayıtlı sayısal değerler farklı bir anlama gelir — **veri bozulması**, hata bile vermez |

---

## 3. İki aşamalı desenler

### 3.1 Kolonu yeniden adlandırma

**Tek `RenameColumn` YASAK.** Yerine üç dağıtım:

| Dağıtım | Migration | Kod |
|---|---|---|
| **1** | Yeni kolonu **nullable** ekle | Her iki kolona **yazar**, yeniyi okur, yeni boşsa eskiye düşer |
| **2** | Eski satırlar için yeniyi **doldur** (backfill) | Değişmez |
| **3** | Eski kolonu **sil** | Yalnızca yeniye yazar |

Dağıtım 1 ve 2 arasında geri alma güvenli: eski kod eski kolonu okuyor ve o
kolon hâlâ dolu.

### 3.2 Kolonu `NOT NULL` yapma

| Dağıtım | Migration | Kod |
|---|---|---|
| **1** | — | Kolonu **her zaman** doldurur |
| **2** | Kalan `NULL`'ları doldur + `NOT NULL` yap | Değişmez |

Dağıtım 2'yi 1'in **aynı** sürümünde yapmak, eski kodun `NULL` yazma
ihtimalini açık bırakır — ve geri alma tam da bu ihtimali gerçeğe çevirir.

### 3.3 Kolonu silme

Silme her zaman **ayrı ve sonraki** bir dağıtımda: önce kodu o kolonu hiç
okumayacak hâle getir, dağıt, çalıştığını gör, **sonra** sil. Aksi hâlde geri
alma imkânsızdır — silinen veri geri gelmez.

### 3.4 Tabloyu silme

Aynı kural, artı: silmeden önce **yedek al** (Vault ya da `pg_dump`). Bir
tabloyu yanlışlıkla silmek geri alınamaz.

---

## 4. Geri alma prosedürü

### 4.1 Karar: geri al mı, ileri düzelt mi?

```
Sorun yalnızca KODDA mı? (migration yok ya da §2'ye uygun)
        │
        ├── EVET → GERİ AL (§4.2). Dakikalar sürer.
        │
        └── HAYIR → migration §2'yi ihlal ediyor mu?
                    │
                    ├── HAYIR → GERİ AL. Şema yeni kalır, eski kod çalışır.
                    │
                    └── EVET  → GERİ ALMA YOK. İLERİ DÜZELT (§4.3).
                                Geri almak veriyi bozar.
```

### 4.2 Kod geri alma

```bash
# 1. Hangi imaj çalışıyor, hangisine dönülecek — ÖNCE yaz.
docker compose -f docker-compose.prod.yml ps
```

```bash
# 2. Önceki etikete dön (imaj etiketi elle verilir; "latest" GERİ ALMA DEĞİLDİR).
NAMINES_TAG=<önceki-sürüm> docker compose -f docker-compose.prod.yml up -d --no-deps api
```

```bash
# 3. Hazır olduğunu DOĞRULA — "up" demesi yetmez.
curl -fsS http://localhost:5000/health/ready
```

**`Database:MigrateOnStartup` üretimde `false` olmalı.** Aksi hâlde geri
aldığınız eski sürüm, açılışta migration koşmaya çalışır ve elindeki
migration listesi veritabanındakinden GERİ olduğu için ya hiçbir şey yapmaz
ya da kafa karıştırıcı bir hata verir. Migration ayrı bir adımdır:

```bash
docker compose -f docker-compose.prod.yml run --rm api --migrate
```

### 4.3 İleri düzeltme (geri alınamaz durum)

1. Hatayı kabul et: **üretim bozuk kalacak**, süre düzeltmeyi yazma süresi.
2. Etkiyi sınırla (özelliği kapat, ilgili ucu 503'e al).
3. Düzeltmeyi yaz, testini yaz, dağıt.
4. Olay sonrası: bu migration §2'yi neden ihlal etti, hangi kontrol kaçırdı.

### 4.4 Veritabanını geri alma — son çare

`dotnet ef migrations remove` **uygulanmış** bir migration'ı geri almaz.
Uygulanmış bir migration'ı geri sarmak:

```bash
dotnet ef database update <BirÖncekiMigrationAdı> \
  --project backend/Namines.Infrastructure --startup-project backend/Namines.API
```

**Bu, `Down()` metodunu çalıştırır ve VERİ SİLEBİLİR.** Eklenen kolon
düşürülürken içindeki veri gider. Önce yedek:

```bash
pg_dump "$ConnectionStrings__DefaultConnection" -Fc -f before-rollback.dump
```

Bu adım yalnızca §2'yi ihlal eden bir migration üretime kaçtığında ve
ileri düzeltme mümkün olmadığında düşünülür.

---

## 5. Migration yazarken kontrol listesi

Üretilen dosyayı **okumadan** commit etme (`dotnet ef migrations add` çıktısı
her zaman istenen şeyi yapmaz):

- [ ] Üretilen `Up()`'ta `DropColumn`, `RenameColumn`, `AlterColumn(nullable: false)`
      ya da `AddUniqueConstraint` var mı? Varsa §3'e göre böl.
- [ ] Yeni kolon nullable mı, ya da varsayılanı var mı?
- [ ] Yeni enum değeri **sona** mı eklendi?
- [ ] `Down()` gerçekten `Up()`'ı geri alıyor mu — yoksa boş mu?
- [ ] Uzun süren bir işlem var mı (büyük tabloda index)? Postgres'te
      `CREATE INDEX CONCURRENTLY` gerekir ve EF onu üretmez; elle yazılmalı.
- [ ] Bu migration eski kodla çalıştırılırsa ne olur? Cevap "bilmiyorum" ise
      dağıtma.

---

## 6. Bugün eksik olanlar (dürüst kayıt)

Bu belge kuralı ve prosedürü yazıyor. **Aşağıdakiler hâlâ yok** ve bu belge
onların yerine geçmez:

| Eksik | Sonucu |
|---|---|
| Kuralı zorlayan otomatik kontrol (CI'da migration tarayıcı) | Kural yazılı ama unutulabilir |
| Mavi-yeşil / rolling update tanımı | Dağıtım sırasında kısa kesinti olur |
| Staging ortamı | Migration ilk kez üretimde çalışır |
| Geri alma tatbikatı | Prosedür hiç denenmedi — yazılı olması çalıştığını kanıtlamaz |

Bunlardan ilki (CI kontrolü) en yüksek getirili: kuralın tek zayıf noktası
insanın hatırlaması.
