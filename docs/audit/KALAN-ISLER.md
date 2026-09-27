# Kalan işler (12.09.2026)

Backlog'daki 64 maddeden 61'i kapandı. Açık kalan 3 madde:

> **2026-09-26 yeniden doğrulama:**
>
> ✅ **B-12 bitmiştir — Oracle yarısı da kapandı** (aynı gün, ikinci tur):
> yeni `Namines.Tests/Integration/OracleIntrospectionTests.cs` gerçek Oracle
> Free 23'e (`gvenzl/oracle-free:23-slim-faststart`, `Testcontainers.Oracle`)
> karşı FK'yı (ON DELETE CASCADE, ON UPDATE = NO ACTION), UNIQUE'i, index'i ve
> identity'nin "bilinmiyor" (null) raporlandığını doğruluyor. Bu test **iki
> gerçek hata buldu:** (1) Oracle'ın tek sayı tipi `NUMBER` ölçeğine
> bakılmadan hep `INT`'e çevriliyordu — `NUMBER(12,2)` bir para kolonu tamsayı
> okunuyor, şema yeniden üretildiğinde küsurat kayboluyordu; artık ölçekli
> olanlar `DECIMAL`, 9 haneyi aşan tamsayılar `BIGINT`. (2) Oracle sayısal
> kolonlarda `CHAR_LENGTH`'i 0 döndürüyor ve bu "bilinen uzunluk" sayılıp
> hassasiyet okunmuyordu; Oracle sorgusunda 0 → NULL.
> Oracle'da CHECK ve varsayılan değer hâlâ BİLEREK okunmuyor (LONG kolonları).
>
> İlk turun kaydı (MSSQL yarısı):
>
> ◐ **B-12 yarı yarıya bitmiştir.** Docker engeli kalktı (makinede Docker
> 29.7 çalışıyor, MSSQL container'ı ayağa kalkıyor) ve **MSSQL yarısı canlı
> doğrulandı:** yeni `Namines.Tests/Integration/MssqlIntrospectionTests.cs`
> gerçek SQL Server 2022'ye karşı FK'yı (ON DELETE CASCADE dahil), UNIQUE/CHECK
> kısıtlarını ve index'leri okuyor. Test **gerçek bir hata da buldu**: MSSQL'de
> `IDENTITY` kolonları `Identity = null` dönüyordu (IDENTITY bir kolon özelliği,
> `COLUMN_DEFAULT` onu taşımaz) — Desk otomatik artan PK'yı zorunlu alan
> sanıyordu. `COLUMNPROPERTY(..., 'IsIdentity')` ile düzeltildi. **Oracle yarısı
> açık:** Oracle imajı makinede yok ve Oracle yolu varsayılan/identity bilgisini
> bilerek okumuyor (LONG kolonu) — `Identity` orada "bilinmiyor" anlamında null.
>
> ✅ **B-38 ve B-56 de bitmiştir (2026-09-27)** — ayrıntı aşağıdaki bölümlerde.
> Aşağıdaki not 2026-09-26 ölçümüdür: **B-38 ve B-56 o gün hâlâ açıktı:** `AIPreferencesModal.tsx` bugün **1653 satır /
> 42 `useState`** (bölünmedi, hafifçe büyüdü); `frontend` ya da `services/desk`
> paketlerinde hiçbir i18n kütüphanesi kurulu değil.

## B-12 — MSSQL/Oracle FK introspection canlı doğrulaması
**Engel:** Docker/disk. MSSQL container'ı en az 2000 MB VM istiyor, mevcut
Docker VM'i 1904 MB. Büyütmek disk kısıtına takılıyor ("docker'a hiç bulaşma
madem yer yok").
**Yapılabilir:** disk açıldığında `docker-compose` ile MSSQL ayağa kaldırıp
`RunTests` projesindeki ilgili testleri canlı çalıştırmak.

## B-38 — `AIPreferencesModal` bölme
> ✅ **Bitmiştir (2026-09-27, `755a3d3`).** Önerilen yol izlendi: önce sekme
> başına state haritası çıkarıldı, sonra bölündü. Modal ~200 satırlık bir kabuk;
> `panels/preferences/` altında 6 sekme bileşeni + `useProfileForm`,
> `useMfaSettings`, `useAiSettings`, `usePlans`, `useLocalStats`, `useOnOpen`.
> State kabuktaki hook'larda kalıyor — sekme değişince bileşen sökülüyor ve
> state orada olsaydı kaydedilmemiş düzenleme kaybolurdu. Tercihler tipli
> `lib/aiAdvancedPrefs.ts`'de (5 test). Canlı: 16 etkileşimin anlık görüntüsü
> taşımadan önce ve sonra birebir aynı. Yol boyunca gerçek bir hata daha
> bulundu: 2FA açmak/kapatmak kullanıcının kendi oturumunu düşürüyordu
> (`e62130b`, `MfaSessionTests`).

**Engel değil, bilinçli erteleme.** 1651 satır, 30+ state. Körlemesine
bölmek (örn. sekmelere ayırmak) state paylaşımını kırma riski taşıyor ve bu
oturumda uçtan uca doğrulanamayacak kadar geniş bir refactor.
**Yapılabilir:** önce hangi state'lerin hangi alt-bölüme ait olduğunu
haritalayan bir analiz, sonra küçük adımlarla (her adımdan sonra manuel
test) bölme.

## B-56 — i18n birleştirme (`frontend` İngilizce, `desk` Türkçe)
> ✅ **Bitmiştir (2026-09-27, `f7aa4fc`).** Ölçünce "desk Türkçe" artık doğru
> değildi — Desk zaten İngilizceye çekilmişti, geriye üç Türkçe kalıntı
> kalmıştı ("Destek", "Yenile", "Veriye git"). Kütüphane KURULMADI: iki dil ve
> düz metin, tipli bir sözlük + `useSyncExternalStore` hook'una sığıyor
> (hydration uyuşmazlığı yok). Desk tam iki dilli; ana uygulamada kabuk
> (üst şerit, çalışma alanı gezinmesi, ayarlar modalinin iskeleti) çevrildi,
> Türkçe orada "beta". Ana uygulamanın geri kalanı İngilizce olduğu için
> `<html lang>` "en" kalıyor, yalnızca çevrilmiş alt ağaçlar `lang="tr"`
> taşıyor — aksi hâlde CSS büyük harfi İngilizceyi "VİEW BLUEPRİNTS" yapıyordu.
> Testler: frontend `lib/i18n/i18n.test.ts` (6), Desk `lib/i18n.test.ts` (10 —
> anahtar, yer tutucu ve işaret eşitliği dahil). Sunucu mesajları İngilizce kalıyor.

**Engel değil, bilinçli erteleme.** Yüzlerce kullanıcıya görünen string var;
tek oturumda elle taşımak yüksek regresyon riski, test kapsamı yok.
**Yapılabilir:** önce i18n altyapısını (kütüphane seçimi + dil seçici +
sözlük dosyası iskeleti) kurup, sonra ekran ekran taşımak — her taşınan
ekranı ayrı commit ve manuel kontrolle.

---

Detaylı gerekçeler ve ölçümler: `docs/audit/22-product-backlog.md` başlığındaki
durum özeti.
