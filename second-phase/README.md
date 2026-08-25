# Second Phase — Namines

> **Bu klasör nedir:** Faz 1 (`new-phase/`) bitti. Burası bundan sonrasının
> kaydı — ne yaptığımız, ne yapacağımız ve neden.
>
> **Tarih:** 2026-08-25 · **Sahip:** Enes Yel

---

## Neden yeni bir klasör

`new-phase/` bir **plan** klasörüydü: 36 numaralı doküman, kod yazılmadan önce
verilmiş kararlar. O plandaki her şey yazıldı ve doğrulandı (CHECKLIST'te G0–G52,
1136 test yeşil).

Burası farklı: **ürün artık çalışıyor.** Bundan sonraki kararlar hayal üzerine
değil, çalışan bir sistemin üzerine veriliyor. Karıştırmamak için ayrı duruyor.

`new-phase/` silinmiyor ve geçersiz değil — mimari kararların gerekçeleri hâlâ
orada. Yeni bir şey yaparken **önce oraya bak**, çoğu tasarım zaten düşünülmüş.

---

## Dosyalar

| # | Dosya | Ne anlatıyor |
|---|-------|--------------|
| 00 | [00-NEREDEYIZ.md](00-NEREDEYIZ.md) | Faz 1 ne teslim etti, ürün bugün ne yapıyor |
| 01 | [01-SIRADAKI-ISLER.md](01-SIRADAKI-ISLER.md) | Ne yapacağız, hangi sırayla, neden o sırayla |
| 02 | [02-REDIS-KARARI.md](02-REDIS-KARARI.md) | Redis: ne, ne zaman, neden şimdi değil |

Senden bekleyen hesap/karar işleri hâlâ
[../new-phase/34-SENDEN-BEKLENENLER.md](../new-phase/34-SENDEN-BEKLENENLER.md)'de.
Orası tek liste olarak kalıyor — ikiye bölmek, birinin unutulması demek olurdu.

---

## Kurallar (Faz 1'de pahalıya öğrenildi, tekrar keşfetme)

1. **"Testler geçiyor" hiçbir şey kanıtlamaz.** G39'da 857 test yeşilken uygulama
   hiç başlamıyordu. Yeni bir şey eklerken **uygulamayı ayağa kaldır ve gerçekten
   kullan.**
2. **Ayar göstermek onu uygulamak demektir.** G52'de 11 gelişmiş ayarın tamamının
   süs olduğu ortaya çıktı — kaydediliyor ama hiç okunmuyorlardı. Yeni bir ayar
   eklerken, onu okuyan yeri de aynı commit'te yaz.
3. **Varsayılan asla veri kaybına doğru düşmemeli.** (`ReferentialActionSql`
   kuralı; `fkAction` varsayılanı bu yüzden RESTRICT.)
4. **Commit'i kullanıcı onaylamadan atma.** Önce `git status` / `git diff` göster.
5. **`.gitignore`'da genel `*.md` kuralı var.** Bu klasör için istisna eklendi
   (`!second-phase/*.md`). Başka bir dizine markdown eklersen `git status`'ta
   gerçekten göründüğünü doğrula.
6. **Disk kritik olabilir.** Docker/build hatası illa kod hatası değil — önce boş
   alana bak.
