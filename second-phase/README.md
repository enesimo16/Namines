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
| 03 | [03-PAZAR-VE-TASARIM-ANALIZI.md](03-PAZAR-VE-TASARIM-ANALIZI.md) | Rekabet, fiyatlandırma, konumlandırma + çalışan uygulamadan ölçülmüş tasarım denetimi |

### Yapılacak işler — önerilen sırayla

| # | Dosya | Ne | Boyut |
|---|-------|-----|-------|
| 04 | [Üretim ekranı](04-LOADING-EKRANI.md) ✅ | Deterministik kapıyı görünür yapar — **farkı anlatan tek ekran** | S |
| 05 | [Plan modu](05-PLAN-MODU.md) ✅ | Konuşarak netleştir, üretmeden önce planı onayla | M |
| 06 | [Veri kaynakları](06-VERI-KAYNAKLARI.md) ✅ kademe 1-2 | URL→API/OpenAPI, extension, localhost. **Bugün yalan söyleyen özellik burada** | M |
| 07 | [Motor dönüşümü](07-MOTOR-DONUSUMU.md) | PostgreSQL→MariaDB, kayıp raporuyla. %70'i hazır | M |
| 08 | [Prompt deneyimi](08-PROMPT-DENEYIMI.md) | Daha çok soru, serbest ekleme, geçmiş, kapsam | S |
| 09 | [Şema alternatifleri](09-SEMA-ALTERNATIFLERI.md) | A/B üret, diff'te karşılaştır, birini seç | M |
| 10 | [Çoklu DB](10-COKLU-DB.md) | Yan yana veritabanları + aralarındaki risk | L |
| 11 | [Koddan şema](11-KODDAN-SEMA.md) | Depodaki modellerden şema çıkar, DB ile karşılaştır | M |
| 12 | [Entegrasyonlar](12-ENTEGRASYONLAR.md) | Supabase önce. Rekabet değil, üstünde durmak | M |
| 13 | [Dağıtım hedefleri](13-DAGITIM-HEDEFLERI.md) | Plesk/cPanel/mobil — rakiplerin bakmadığı kitle | S |

### Ayrı ürünler — kayıt için, şimdi başlanmayacak

| # | Dosya | Neden ayrı |
|---|-------|-----------|
| 14 | [Development + Hosting](14-AYRI-URUN-DEVELOPMENT-HOSTING.md) | Barındırma seni altyapı sağlayıcısına çevirir: 7/24 nöbet, kötüye kullanım, yasal sorumluluk |
| 15 | [Flow (otomasyon)](15-AYRI-URUN-FLOW.md) | Kuyruk/teslim garantisi altyapısı ister, çekirdek farkla ilgisi yok, pazar kalabalık |

> **Bilerek reddedilenler** ayrıca her dosyanın sonunda "🔴 Yapılmayacak"
> başlığı altında duruyor. Bir şeyi yapmama kararı da bir karardır ve
> gerekçesi kaybolursa altı ay sonra yeniden tartışılır.

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
