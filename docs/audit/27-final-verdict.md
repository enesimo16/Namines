# 27 — Nihai karar

## Soru

> *"Bu proje gerçekten production-grade bir Database Management ürünü mü?"*

## Cevap — denetim anında

# ALMOST

> ## ✅ GÜNCELLEME (düzeltme turundan sonra)
>
> Aşağıdaki `YES` engellerinin **altısı da kapandı**:
>
> | `YES` engeli | Durum |
> |---|---|
> | SEC-005 JWT fallback anahtarı | ✅ |
> | SEC-004 Mermaid XSS | ✅ |
> | SEC-003 Sahte güvenlik özelliği | ✅ |
> | SEC-001 Denetimsiz keyfi SQL | ✅ |
> | AUTHZ-004 Cross-site CSRF | ✅ |
> | DEVOPS-001 Üretim dağıtımı | ✅ |
> | Şifreleme anahtarı kurtarma planı | 🟡 **Hâlâ yok — belge işi, kod değil** |
>
> **Güncel karar: YES'e çok yakın.** Kalan tek teknik engel bir belge:
> `Vault:BackupEncryptionKey`'in nerede ve kaç kopya saklandığı yazılı değil.
> O anahtar kaybolursa her yedek kalıcı olarak okunamaz hâle gelir — ve bu,
> yazılmakla çözülecek bir şey.
>
> Kalan P1'ler (frontend testleri, MSSQL/Oracle doğrulaması, 128 lint hatası)
> **kaliteyi** etkiliyor, üretime çıkmayı engellemiyor.

İki nedenle — biri teknik, biri kategorik.

---

## 1. Teknik neden: temel sağlam, kapı açık kalmış

Bu kod tabanı, denetimin beklediğinden **iyi**. Somut ve ölçülmüş:

- 100.000 satırda **sıfır** `TODO` / `FIXME` / `HACK`
- **Sıfır** derleme uyarısı
- **1.480** backend testi, gerçek veritabanı konteynerlerine karşı, CI'da koşuyor
- **Sıfır** ham EF SQL; dinamik SQL'in tamamı tek bir allowlist'ten geçiyor
- Modül sınırı **derleyiciyle** korunuyor, belgeyle değil
- Yedekler şifreli **ve geri yüklenebilirlikleri kanıtlanıyor** — temiz bir
  sunucuya gerçekten restore edilerek
- Kod yorumları kararın **gerekçesini** yazıyor; bu denetim boyunca defalarca
  "tuhaf görünen desen"in yanında neden öyle olduğu yazılıydı

Bu, "production-grade" tanımının **çoğunu** karşılıyor.

Ama üretime çıkmaya hazır değil, çünkü:

- **6 adet P0 açık** (bkz. `26` STOP SHIP listesi)
- **Üretim dağıtımı depoda tanımlı değil** — sistem yeniden kurulamıyor
- **Yedek şifreleme anahtarının kurtarma planı yok** — kaybolursa tüm yedekler
  okunamaz hâle gelir
- **Frontend'in üçte biri test edilmiyor**

Bunların hiçbiri mimari sorun değil. Hiçbiri "yeniden yaz" gerektirmiyor.
**İkisi 20 dakikada kapanıyor.** Toplam P0 eforu yaklaşık bir hafta.

**Yani:** `YES`'e uzaklık **bir hafta**, bir yeniden yazım değil.

---

## 2. Kategorik neden: soru yanlış kategoriye soruluyor

Denetim talebi bu ürünü "Database Management / Database GUI" olarak tanımlıyor
ve DBeaver, DataGrip, TablePlus ile karşılaştırılmasını istiyor.

**Kod bu tanımı desteklemiyor.** Ürün şunları yapıyor:

- Şema tasarımı (görsel + metin dili)
- Sunucu taraflı **branch**
- Risk sınıflandırmalı **Change Request + onay politikası**
- Değişiklik **etki analizi** + AI açıklaması
- Otomatik REST API (izin modeli + denetim kaydı ile)
- Şifreli yedek + **kanıtlanmış** geri yükleme
- Yönetilen veritabanı sağlama
- Kod üretimi / eject, MCP sunucusu, CLI

Bunların hiçbiri DBeaver'ın yaptığı iş değil. Buna karşılık DBeaver'ın temel
özellikleri — sorgu geçmişi, kaydedilmiş sorgu, otomatik tamamlama, klavye
kısayolları — Namines'te **yok**.

**Sonuç:** "production-grade bir Database Management ürünü mü?" sorusuna
DBeaver ölçeğiyle bakılırsa cevap **NO** olur — ama bu, yanlış cetvelle ölçmek
olur. Doğru soru şu:

> *Production-grade bir **veritabanı değişiklik yönetişimi ve koruma** platformu mu?*

O soruya cevap: **ALMOST — bir haftalık iş uzağında.**

---

## Gerekçelendirme: neden `NO` değil

`NO` demek için şunlardan biri gerekirdi ve **hiçbiri yok**:

| `NO` gerektiren durum | Bulundu mu |
|---|---|
| Sömürülebilir SQL enjeksiyonu | ❌ Bulunamadı — allowlist tutarlı, testli |
| Kimlik doğrulama bypass'ı (yapılandırmadan bağımsız) | ❌ Bulunamadı |
| IDOR / yetkisiz nesne erişimi | ❌ Bulunamadı |
| Depoda sızmış sır | ❌ Bulunamadı (git geçmişi dahil) |
| Mimari çürüme | ❌ Aksine, sınırlar derleyiciyle korunuyor |
| Veri kaybına yol açan hata | ❌ Bulunamadı |

## Gerekçelendirme: neden `YES` değil

`YES` demek için şunların çözülmüş olması gerekirdi:

| `YES` engeli | Durum |
|---|---|
| SEC-005 — yanlış ortamda bilinen JWT anahtarı | ❌ Açık |
| SEC-004 — paylaşılan şemada saklı XSS | ❌ Açık |
| SEC-003 — sahte güvenlik özelliği | ❌ Açık |
| SEC-001 — denetimsiz keyfi SQL | ❌ Açık |
| AUTHZ-004 — cross-site CSRF | ❌ Açık |
| DEVOPS-001 — üretim dağıtımı yeniden kurulamıyor | ❌ Açık |
| Şifreleme anahtarı kurtarma planı | ❌ Yok |

---

## Bu projeyi diğerlerinden ayıran şey

Denetim raporlarında genelde şu cümle yazılır: *"Ekip kaliteyi bilmiyor."*
Burada tersi geçerli — ve bu bulgu, bulguların kendisinden önemli.

Bu depoda **doğru standart zaten uygulanmış**, sadece her yerde değil:

| Standart | Uygulandığı yer | Uygulanmadığı yer |
|---|---|---|
| Ham sürücü hatasını sızdırma | `GatewayKeyController` (gerekçesi yazılı) | `DatabaseExecutorController` |
| Her işlemi denetime yaz | `ChangeRequest`, `Gateway` | Executor, introspection |
| Yıkıcı işlemde nesne adını yazdır | Vault geri yükleme | Executor |
| Test yaz | `services/desk` (99 test) | `frontend` (0 test) |
| Fail-closed varsayılan | Vault şifreleme anahtarı | JWT anahtarı (kısmen) |

Yani bulguların çoğu "bilgi eksikliği" değil, **tutarlılık eksikliği**. Bu,
düzeltilmesi en kolay hata türü: çözüm zaten depoda, yalnızca ikinci yere
uygulanacak.

---

## Tek cümlelik karar

> **Namines, mühendislik disiplini beklenenin üstünde olan, mimarisi sağlam,
> testleri gerçek sistemlere karşı koşan bir platform. Üretime çıkmasını
> engelleyen şey mimari değil, altı adet noktasal açık ve eksik bir dağıtım
> tanımı — toplam bir haftalık iş. Ama ürünün kendisini doğru kategoride
> anlatması, o bir haftalık işten daha önemli.**

---

## Sonraki adım

Onay verilirse, `23-quick-wins.md`'deki ilk iki madde ile başlanmalı:
**JWT anahtar kapısı (15 dk) ve Mermaid `strict` (5 dk).**
Yirmi dakikada, risk kaydındaki iki en yüksek riskten biri ve dördüncüsü kapanır.
