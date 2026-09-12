# Kalan işler (12.09.2026)

Backlog'daki 64 maddeden 61'i kapandı. Açık kalan 3 madde:

## B-12 — MSSQL/Oracle FK introspection canlı doğrulaması
**Engel:** Docker/disk. MSSQL container'ı en az 2000 MB VM istiyor, mevcut
Docker VM'i 1904 MB. Büyütmek disk kısıtına takılıyor ("docker'a hiç bulaşma
madem yer yok").
**Yapılabilir:** disk açıldığında `docker-compose` ile MSSQL ayağa kaldırıp
`RunTests` projesindeki ilgili testleri canlı çalıştırmak.

## B-38 — `AIPreferencesModal` bölme
**Engel değil, bilinçli erteleme.** 1651 satır, 30+ state. Körlemesine
bölmek (örn. sekmelere ayırmak) state paylaşımını kırma riski taşıyor ve bu
oturumda uçtan uca doğrulanamayacak kadar geniş bir refactor.
**Yapılabilir:** önce hangi state'lerin hangi alt-bölüme ait olduğunu
haritalayan bir analiz, sonra küçük adımlarla (her adımdan sonra manuel
test) bölme.

## B-56 — i18n birleştirme (`frontend` İngilizce, `desk` Türkçe)
**Engel değil, bilinçli erteleme.** Yüzlerce kullanıcıya görünen string var;
tek oturumda elle taşımak yüksek regresyon riski, test kapsamı yok.
**Yapılabilir:** önce i18n altyapısını (kütüphane seçimi + dil seçici +
sözlük dosyası iskeleti) kurup, sonra ekran ekran taşımak — her taşınan
ekranı ayrı commit ve manuel kontrolle.

---

Detaylı gerekçeler ve ölçümler: `docs/audit/22-product-backlog.md` başlığındaki
durum özeti.
