# last-review — 2026-09-30 güvenlik ve doğruluk incelemesi

Bu klasör, 2026-09-29/30'da yapılan tam tur güvenlik + doğruluk + çok-kiracılı
izolasyon incelemesinin çıktılarını taşıyor.

| Dosya | İçerik |
|---|---|
| [01-INCELENMEYENLER.md](01-INCELENMEYENLER.md) | **Bakılmamış** alanlar. "Temiz" değil — incelenmedi. Bir sonraki turun iş listesi. |
| [02-BULUNAN-HATALAR.md](02-BULUNAN-HATALAR.md) | Bulunan her şey: 1 CRITICAL, 7 HIGH, 17 MEDIUM, 5 LOW, 8 spekülatif. Düzeltme durumu burada takip ediliyor. |
| [03-DOGRULANAN-KONTROLLER.md](03-DOGRULANAN-KONTROLLER.md) | İncelemede **doğru** çıktığı kod okunarak kanıtlanan kontroller. Tekrar denetlenirken zaman kaybedilmesin diye. |

## Durum (2026-10-01)

**Hepsi ele alındı.** Ayrıntı ve her bulgunun ne olduğu
[02-BULUNAN-HATALAR.md](02-BULUNAN-HATALAR.md)'de; proje tarihçesindeki kaydı
`new-phase/CHECKLIST.md` §G59.

| Grup | Kapandı | Kısmen |
|---|---|---|
| CRITICAL (1) | C-1 | — |
| HIGH (7) | H-1 … H-7 | — |
| MEDIUM (17) | M-1 … M-14, M-16, M-17 | M-15 |
| LOW (5) | L-1 … L-5 | — |

**Açık bırakılan bulgu yok.** İlk turda ürün/altyapı kararı bekleyen dördü
(M-8'in AAD bağlaması, M-11, M-16'nın geniş hâli, L-1, L-4) ikinci turda
kullanıcının onayıyla kapatıldı — hepsinde **güvenli varsayılan + açık opt-in**
şekli seçildi, yani meşru kullanım tek satır yapılandırmayla geri geliyor.

**Tek kısmi kalan M-15:** jeton ömrü 7 günden 12 saate çekildi ve
`namines:purpose` claim'iyle işaretlendi, ama hâlâ projeye bağlı değil. Gerçek
kapsamlama her ucun bu claim'e göre gözden geçirilmesini gerektiriyor; tek başına
eklemek, kapsamlıymış gibi görünen ama olmayan bir jeton üretirdi.

**Doğrulama:** backend suite **yeşil**, 4 EF migration gerçek PostgreSQL'e uygulandı,
uygulama lokalde ayakta ve test hesabıyla uçlar denendi (C-1 enjeksiyonu canlı uçta
nötr, M-9 anonim yanıtı yalnızca `{ok}`, M-15 jetonu 12 saat). Düzeltmelerin kendi
ürettiği **üç regresyon** 02'de ayrı bir bölümde — ikisi mevcut testlerle, biri
localhost doğrulamasıyla yakalandı.

## Yöntem ve güven seviyesi

İnceleme altı paralel denetçiye bölündü (yetkilendirme/izolasyon · SQL/sürücüler ·
SSRF · Vault/Ground/Docker · state machine/faturalama · AI/MCP/GitHub/frontend).
**Beşi rapor teslim etti**; SQL/sürücüler denetçisi DDL üreticilerine geçerken
oturum kotasına takıldı — bu yüzden 01'deki ilk madde en büyük boşluk.

Her **CRITICAL ve HIGH** bulgusu, raporu yazan tarafından kod yolu izlenerek
ayrıca doğrulandı. MEDIUM'ların bir kısmı tek denetçinin raporuna dayanıyor ve
02'de öyle işaretli.

## Bu incelemenin kendi dersi

Önceki turda kapatılan 12 bulgunun **11'i eksiksiz**. Biri (`BuildIdentifier`
düzeltmesi) bir regresyon yarattı ve bu turda HIGH-7 olarak geri geldi:
*bir düzeltme, dayandığı önermeyi başka bir yerde çürütebilir.* Düzeltme
yaparken "bu adı/değeri başka kim türetiyor?" sorusu artık kontrol listesinde.
