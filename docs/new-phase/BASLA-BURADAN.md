# BAŞLA BURADAN — Namines'in Bütün Hikâyesi

> **Bu belge ne işe yarar:** Diğer 27 dosya referans niteliğinde — ihtiyaç duydukça açacaksın. Bu dosya ise **baştan sona okunmak** için yazıldı. Ne yapacağımızı, neden yapacağımızı ve işin sonunda elinde ne olacağını anlatıyor. Her çalışmaya oturduğunda buraya dönüp "büyük resim neydi" diye bakabilirsin.
>
> **Okuma süresi:** ~20 dakika. Tablo yok, anlatı var.
> **Son güncelleme:** 2026-08-08 (strateji notu: 2026-08-10)
>
> ⚠️ **2026-08-10:** Aşağıdaki anlatı hâlâ doğru — Faz 0 gerçekten bitti, "hiçbir
> özellik silinmiyor" prensibi hâlâ geçerli. Ama "nereye gidiyoruz" bölümündeki
> sıralama (Console/Gateway önce) [27-LIFECYCLE-PIVOT.md](27-LIFECYCLE-PIVOT.md) ile
> düzeltildi: **Impact Analysis + Database Change Review** artık önce geliyor,
> çünkü asıl fark orada — Claude Code/Codex gibi agent'lar zaten iyi kod/şema
> üretiyor, üretmedikleri şey "bu değişiklik güvenli mi, kanıtla" katmanı. Devam
> etmeden önce 27'yi oku.

---

## 1. Bugün elinde tam olarak ne var

Repoyu çektim ve satır satır inceledim. Gerçek envanter şu:

Namines şu anda **37.000 satırlık, çalışan bir yazılım.** İki aylık yoğun bir çalışmanın ürünü ve bu, göz ardı edilecek bir şey değil. İçinde 21 API controller'ı, 137 C# dosyası, 48 React bileşeni, 16 state store'u ve 7 özel hook var. Backend .NET 8 üzerinde katmanlı bir mimariyle yazılmış — API, Core, Infrastructure ayrımı doğru yapılmış. Frontend Next.js 16 ve React 19 ile, React Flow tabanlı gerçek bir canvas'a sahip.

Yaptığı işler gerçekten çalışıyor: doğal dilden şema üretiyor, altı farklı veritabanı motoruna DDL yazıyor, EF Core ve Prisma modelleri çıkarıyor, PDF veri sözlüğü hazırlıyor, Mermaid diyagramları üretiyor, canlı veritabanına bağlanıp tersine mühendislik yapabiliyor, sesli giriş alabiliyor, görselden şema okuyabiliyor, gerçek zamanlı işbirliği yapıyor, Stripe ile ödeme alıyor.

Güvenlik tarafında da ciddi bir emek var: JWT httpOnly cookie'de tutuluyor, SSRF koruması yazılmış, BYOK anahtarları AES-256-GCM ile şifreleniyor, rate limiting kullanıcı bazlı partition'lanmış, prompt injection'a karşı sertleştirme yapılmış. Bunlar acemi işi değil.

**Yani ortada bir prototip değil, bir ürün var.** Bu planın tamamı sıfırdan başlamak üzerine değil — var olanın üzerine inşa etmek üzerine kurulu.

---

## 2. Peki sorun ne

Üç şey.

**Birincisi: ürettiğin şey, insanların bedavaya elde edebildiği bir şey.** "Bana e-ticaret şeması yaz" demek için Namines'e gitmene gerek yok — Cursor, Claude, ChatGPT bunu on saniyede yapıyor, üstelik geliştiricinin zaten açık olan penceresinde. Şema tasarımının maliyeti ilk taslağı yazmak değil. Asıl maliyet, o taslağı gerçek bir sisteme dönüştürmek ve altı ay sonra değiştirmek zorunda kaldığında hayatta kalmak.

**İkincisi: ürün tek seferlik kullanılıyor.** Kullanıcı geliyor, şemasını üretiyor, DDL'i indiriyor, gidiyor. Geri dönmek için bir sebebi yok. Verisi orada değil, ekibi orada değil, iş akışı orada değil. Bu, aylık abonelikle satılabilecek bir ürün değil — bir yardımcı araç.

**Üçüncüsü: temelde bir ifade gücü problemi var.** `DatabaseSchema` modelinde index diye bir kavram yok. Unique constraint yok. Check constraint yok. Bileşik anahtar yok. Ve altı DDL üreticinin hepsi her yabancı anahtara sabit `ON DELETE CASCADE` yazıyor — bu, SQL Server'da iki farklı yoldan aynı tabloya ulaşan her şemada (yani gerçek hayattaki çoğu şemada) DDL'in tamamen reddedilmesine yol açıyor. Bir veritabanı tasarım aracının index üretememesi, bir hesap makinesinin çarpma yapamaması gibi.

Bu üçünün ortak sonucu: iyi yazılmış ama satılamayan bir yazılım.

---

## 3. Nereye gidiyoruz

Tek cümlede:

> **Şema değişir, geri kalan her şey kendini günceller.**

Namines artık bir çizim aracı değil, **şemadan çalışan bir backend üreten ve onu canlı tutan bir platform** olacak. Kullanıcı fikrini anlatacak, Namines şemayı tasarlayacak, gerçek bir veritabanı ayağa kaldıracak, üstüne çalışan bir REST/GraphQL API açacak ve ekibin kullanabileceği bir yönetim paneli üretecek. Sonra kullanıcı şemayı değiştirdiğinde, bunların hepsi kendiliğinden güncellenecek.

Bunu üç katman olarak düşün.

**Birinci katman — Tasarım.** Bugün zaten sahip olduğun şey. Canvas, AI şema üretimi, DDL derleme, dokümantasyon. Bunlar kalıyor ve güçleniyor. Kullanıcının ilk temas noktası burası ve düşük taahhüt gerektiriyor: sadece bir fikir yazıyorsun.

**İkinci katman — Veri.** Yeni. Şemayı gerçek bir veritabanına dönüştürüyoruz. Namines kullanıcı için PostgreSQL (veya MySQL, SQL Server) sağlıyor, DDL'i uyguluyor, örnek veri dolduruyor, yedekliyor, migration'ları güvenle yürütüyor. Artık kullanıcının verisi platformda yaşıyor. Bu, taahhüdün ikinci basamağı.

**Üçüncü katman — Uygulama.** En kritik yeni parça. Bu veritabanının üstüne otomatik olarak bir API ve bir yönetim paneli koyuyoruz. Kullanıcı hiçbir konfigürasyon yapmadan, şemasındaki her tablo için çalışan bir liste ekranı, düzenleme formu, filtre, arama ve ilişki navigasyonu elde ediyor. Rol tanımlayıp destek ekibini içeri alabiliyor. Kim neyi değiştirdi görebiliyor. Bu, taahhüdün son basamağı ve terk edilmesi en zor olanı.

Bu üç katmanın hepsi **tek bir doğruluk kaynağından** türetiliyor: NSL adını verdiğimiz yeni şema dili. Namines'in savunulabilir tarafı tek tek özellikler değil — bu üç katmanın birbiriyle sürekli senkron kalması. Bunu kopyalamak pahalı.

---

## 4. Kullanıcı gözünden önce ve sonra

Bunu somutlaştırmak en iyisi.

**Bugün:** Bir geliştirici Namines'e giriyor. "Kargo takip sistemi" yazıyor. Güzel bir şema çıkıyor. Canvas'ta birkaç düzeltme yapıyor. PostgreSQL DDL'ini indiriyor. Sonra kendi bilgisayarında Docker'da bir Postgres kaldırıyor, DDL'i yapıştırıyor, çalıştırıyor — belki hata alıyor çünkü cascade yolları çakışmış. Elle düzeltiyor. Sonra Prisma şemasını elle yazıyor. Sonra bir admin panel gerektiğini fark ediyor, Retool'a bakıyor, bağlıyor, her tablo için elle ekran kuruyor. İki gün sonra şemaya bir kolon eklemesi gerekiyor ve dört ayrı yerde aynı işi tekrar yapıyor. Namines'e bir daha uğramıyor.

**Faz 2 sonrası:** Aynı geliştirici "kargo takip sistemi" yazıyor. Şema çıkıyor, düzeltiyor. **"Yayınla"** diyor. Doksan saniye içinde eline üç adres geliyor: bir veritabanı bağlantı dizesi, bir API adresi, bir yönetim paneli adresi. Panele giriyor, gerçekten çalışan bir arayüz görüyor — gönderiler listesi, filtreleme, yeni kayıt formu, müşteriye tıklayınca o müşterinin gönderileri. Operasyon ekibinden iki kişiyi "sadece gönderi durumu güncelleyebilir" rolüyle davet ediyor. Kendi uygulamasından API'ye bağlanıyor, TypeScript tipleri hazır geliyor. İki gün sonra kolon ekliyor: risk analizi çıkıyor ("bu güvenli, kilit yok"), onaylıyor, migration uygulanıyor, panel ve API kendiliğinden güncelleniyor, TypeScript tipleri yenileniyor, GitHub'daki PR'a bot yorum bırakıyor.

Bu geliştirici bir daha ayrılmıyor. Çünkü verisi orada, ekibi orada, iş akışı orada.

Aradaki fark ürünün kaderi kadar büyük: birinci senaryoda ayda 9 dolar isteyebilirsin ve kullanıcı bir ay sonra iptal eder. İkinci senaryoda kişi başı 39 dolar istersin, ekip büyüdükçe gelir kendiliğinden artar ve iptal etmek acı verir.

---

## 5. Ne inşa edeceğiz — sırayla

Planı beş faza böldüm. **Her fazın sonunda satılabilir bir ürün olması şart.** "On iki ay sonra bitecek" diye bir şey yok; her durakta elinde yayınlanabilir bir şey olacak.

### Faz 0 — Temeli sağlamlaştırma (yaklaşık 6 hafta)

Burada tek bir yeni özellik yazmıyoruz. Var olanı kırık olmaktan çıkarıyoruz.

NSL'in çekirdeğini kuruyoruz: index, unique, check, bileşik anahtar ve — en önemlisi — yabancı anahtar silme politikası. Sabit `CASCADE` gidiyor, yerine varsayılanı `NO ACTION` olan gerçek bir seçim geliyor ve SQL Server'ın çoklu cascade yolu hatasını derleme öncesinde yakalayan bir analizör ekleniyor. Altı DDL üreticisini bu yeni model üzerine yeniden yazıyoruz.

Sonra test altyapısı. Şu anda sıfır test var ve ürünün tamamı kod üretimi — yani doğruluk ürünün ta kendisi. Golden-file testleri kuruyoruz (her şema fixture'ı için beklenen çıktı kaydediliyor, değişirse test kırılıyor) ve Testcontainers ile **gerçek veritabanı motorlarında** üretilen DDL'i çalıştırıyoruz. Bir de round-trip testi: NSL'den DDL üret, gerçek DB'de çalıştır, geri oku, başladığın yerle karşılaştır. Bu tek test tip eşleme hatalarının neredeyse hepsini yakalar.

Altyapı tarafında: control plane veritabanı SQLite'tan PostgreSQL'e geçiyor (SQLite tek dosya olduğu için ikinci bir sunucu açamıyorsun), Serilog dosyaya yazmayı bırakıp stdout'a geçiyor, startup'taki `Database.Migrate()` ayrı bir job'a taşınıyor, SignalR'a Redis backplane ve kimlik doğrulama ekleniyor, ve `docker.sock` mount'u tamamen kaldırılıyor — bu sonuncusu host üzerinde root yetkisi demek ve çok kiracılı bir SaaS'ta kabul edilemez.

**Faz 0 sonunda elinde ne var:** Aynı özellikler, ama artık ürettiğin DDL gerçekten çalışıyor, index üretebiliyorsun, testlerin var ve altyapı ölçeklenebiliyor. Bunu tek başına yayınlayabilirsin: *"Namines artık altı motorda %100 doğrulanmış DDL üretiyor."* Bu ciddi bir mesaj ve bugünkü halinle söyleyemeyeceğin bir şey.

### Faz 1 — Şema aracı olarak sınıfın en iyisi (yaklaşık 8 hafta)

NSL'i tamamlıyoruz: enum, view, hesaplanmış kolon, RLS politikaları, açıklamalar, namespace desteği. Doğrulama motorunu kuruyoruz — 25 kural, çoğu tek tıkla otomatik düzeltilebilir. Diff ve migration planlayıcı geliyor: iki şema sürümü arasındaki farkı çıkarıp her operasyonu risk seviyesine göre sınıflandırıyor.

DBML import/export ekliyoruz. Bu küçük görünen ama stratejik bir hamle: dbdiagram.io'nun kullanıcı tabanı devasa ve DBML onların formatı. "dbdiagram'dan tek tıkla getir" diyebilmek, oradaki kullanıcıyı buraya çekmenin en ucuz yolu.

Canvas tarafında Yjs tabanlı CRDT'ye geçiyoruz. Bugünkü işbirliği aslında bir broadcast rölesi — son yazan kazanıyor ve veri kaybediyor. CRDT ile çakışma matematiksel olarak imkânsız hale geliyor, undo/redo sınırsız ve çok kullanıcılı oluyor, çevrimdışı çalışma mümkün oluyor. Branch'ler de artık cihazda değil sunucuda tutuluyor.

Bir de metin modu ekliyoruz: Monaco editörle `.nsl` dosyasını doğrudan yazabiliyorsun, canvas ve metin iki yönlü senkron. Deneyimli geliştiricilerin çoğu görsel editörden hoşlanmaz; bu, "bu araç bana göre değil" itirazını ortadan kaldırıyor.

AI tarafında en kritik ekleme **eval harness**: her prompt değişikliğinin kalitesini ölçen otomatik test sistemi. Şu anda AI'ın iyi olup olmadığını ölçen hiçbir şey yok. Bundan sonra her PR'da bir skor kartı çıkacak ve regresyon varsa merge edilemeyecek. Ayrıca varsayılan model 8 milyar parametreliden 70B/Sonnet sınıfına çıkıyor — ücretsiz kullanıcıya "az ama kaliteli" veriyoruz, "çok ama kötü" değil.

**Faz 1 sonunda elinde ne var:** Piyasadaki en ifade gücü yüksek şema tasarım aracı. dbdiagram'dan import edebiliyor, ondan daha fazlasını ifade edebiliyor, AI kalitesi ölçülüyor ve kanıtlanabiliyor. Bu noktada 9-14 dolar bandında satılabilir bir ürünün var ve **bu bile geçerli bir sonuç.** Buradan devam etmesen bile elinde saygın ve sürdürülebilir bir şey olur.

### Faz 2 — Veri katmanı (yaklaşık 8 hafta)

Şimdi gerçek veritabanları geliyor.

Provisioning'i **kendimiz yazmıyoruz, satın alıyoruz.** Neon'un API'sini kullanıyoruz — serverless PostgreSQL, gerçek branching desteği, kullanılmayınca sıfıra inen maliyet. Kendi PostgreSQL kümeni işletmek tek kişilik bir ekip için ürün geliştirmeyi öldürür. Ama sağlayıcıya kilitlenmemek için `IDatabaseProvider` diye bir soyutlama koyuyoruz; Neon, PlanetScale, Azure SQL ve kendi Kubernetes kurulumumuz aynı arayüzü konuşuyor.

Docker sandbox özelliği ölmüyor — **Ephemeral Database** olarak geri geliyor. Ama bu sefer docker.sock ile değil, Kubernetes Job API'si üzerinden ve gVisor izolasyonuyla. Ağ çıkışı tamamen kapalı, kaynak limitleri var, 60 dakika sonra kendini siliyor. Üstelik önceden ısıtılmış bir havuz tuttuğumuz için 90 saniye yerine 20 saniyede hazır oluyor.

Migration yürütme motoru burada kritik. Riskli operasyonları otomatik olarak güvenli desenlere çeviriyoruz: index oluşturma `CONCURRENTLY` ile kilitsiz yapılıyor, foreign key ve check constraint'ler `NOT VALID` + `VALIDATE` ikilisiyle ekleniyor, her migration'a `lock_timeout` konuyor. O tek satır `lock_timeout` çoğu üretim kazasını önler. Yıkıcı işlemlerden önce otomatik yedek alınıyor ve rollback script'i her zaman önceden üretiliyor.

Smart Seed, Data Factory'ye dönüşüyor: artık referans bütünlüğü garantili, milyonlarca satır üretebiliyor, dağılım kontrolü var ("siparişlerin %70'i ödendi"), deterministik (aynı seed → aynı veri) ve 40 yerel ayarı destekliyor. AI sadece "bu kolon ne tür veri tutuyor" tahmini için kullanılıyor; üretimin kendisi deterministik ve bedava.

**Faz 2 sonunda elinde ne var:** Şemadan gerçek, çalışan bir veritabanı. İlk büyük farklılaşma. Artık dbdiagram'la aynı kategoride değilsin.

### Faz 3 — Uygulama katmanı (yaklaşık 12 hafta) — **planın kalbi**

Burası ürünü değiştiren yer.

**Gateway** — tenant veritabanının üstünde çalışan otomatik API. Her tablo için CRUD endpoint'leri, zengin bir filtre dili (`?status=in.(paid,shipped)&total=gte.100&order=placed_at.desc&expand=user`), OpenAPI 3.1 spesifikasyonu, GraphQL endpoint'i, API anahtarı yönetimi. Sözdizimini bilinçli olarak PostgREST/Supabase'e benzetiyoruz ki geliştiricinin öğrenme maliyeti sıfır olsun. Güvenlik iki katmanlı: uygulama seviyesinde rol filtresi ve kolon maskeleme, veritabanı seviyesinde RLS. Birinde bug olsa bile veri sızmıyor.

**Console** — otomatik yönetim paneli. Burası en çok mühendislik gerektiren parça ve en çok değer üreten parça. Tek bir Next.js uygulaması var; on bin proje için on bin uygulama yok. Uygulama metadata'yı okuyor ve kendini şekillendiriyor. Her kolon tipini doğru widget'a eşliyor: tarih kolonu tarih seçiciye, enum kolonu renkli rozetli açılır listeye, JSON kolonu Monaco editöre, yabancı anahtar kolonu uzaktan arama yapan bir combobox'a — ve o combobox hedef tablonun anlamlı bir etiketini gösteriyor, çıplak ID değil.

Sadece tablo listesi değil, **sayfa deseni** de otomatik seçiliyor: bir tablonun kendisine ait çocuk tablosu varsa ana-detay görünümü, kendine referans veren bir FK varsa ağaç görünümü, enum durum kolonu varsa kanban seçeneği, tek satır tutan tablo için singleton ekranı. Sadece iki FK'dan oluşan ara tablolar için ayrı sayfa açılmıyor, ilişki editörüne dönüşüyor. "Sıfır konfigürasyonda anlamlı bir panel" vaadi buradan geliyor.

Üstüne rol tabanlı erişim kontrolü: hangi rol hangi tabloyu görür, hangi kolonu düzenleyebilir, hangi satırları görebilir. Ve denetim kaydı — kim, ne zaman, neyi, neyden neye değiştirdi. Kurumsal satışta en çok sorulan özellik budur.

**Ve eject.** Kullanıcı istediği an paneli kaynak kod olarak indirebiliyor: Next.js, React, Blazor veya Streamlit. Faz 1'deki Streamlit export özelliği tam olarak burada yaşıyor ve artık zayıf bir teslim biçimi değil, güçlü bir pazarlama argümanı: *"Lock-in yok. İstediğin an gerçek koda çık."* Retool ve Xano'nun yapamadığı şey bu ve kurumsal alımlarda en büyük itirazı ortadan kaldırıyor.

**Faz 3 sonunda elinde ne var:** Ürünün tamamı. Fikirden çalışan backend'e, admin paneline ve API'ye. Satılabilir ilk gerçek sürüm. **Buraya kadar yaklaşık 9 ay.**

### Faz 4 ve 5 — Ekip ve kurumsal (yaklaşık 18 hafta)

Bunlar ancak Faz 3'ün verileri tezi doğrularsa yapılmalı.

Faz 4'te branch veritabanları geliyor (PR açınca otomatik önizleme DB'si, kapanınca siliniyor), GitHub App geliyor (her PR'a risk raporu ve rollback linki bırakan bot), CLI geliyor, blueprint pazarı ve public şema sayfaları geliyor — bunlar büyüme döngüleri.

Faz 5'te on-prem agent (Bridge), PII maskeleme, SSO, Türkiye bölgesi ve sürekli çalışan DBA danışmanı var. Bunlar kurumsal satış paketini oluşturuyor.

---

## 6. Hiçbir şey silinmiyor

Bu önemli, çünkü ilk analizimde "şunları kes" demiştim ve sen "hiçbir özelliği yitirmek istemiyorum" dedin. Planı ona göre kurdum. İşte bugün var olan her şeyin nereye gittiği:

Docker sandbox → Data Plane'in ephemeral katmanı oldu, güvenli bir şekilde yeniden yazıldı. Streamlit admin app export → Console'un eject hedeflerinden biri oldu, üstüne Next.js ve Blazor eklendi. Sesli giriş → Copilot'un voice mode'u oldu, Türkçe desteği ayrıştırıcı özellik olarak vurgulanıyor. Oracle ve MariaDB → "extended engines" katmanında kaldı, golden testleri var ama öncelik düşük. Multiplayer → CRDT ile gerçek bir işbirliği sistemine dönüştü. Şablon galerisi → Blueprint Hub oldu ve hem SEO hem topluluk motoru işlevi görüyor. CI schema-diff script'i → GitHub App'e terfi etti. DBA rozeti → gerçek metriklerle besleniyor ve README'lerde görünerek viral kanal oluyor. PDF, Mermaid, README üretimi → Docs Engine oldu. BYOK ve AES şifreleme → Secret Vault oldu. Token havuzu → anlaşılır bir kredi sistemine dönüştü. Feedback widget, help center, tur, ⌘K paleti, undo/redo, canvas arama — hepsi duruyor.

Toplam: 38 mevcut özellik korundu, 21'i geliştirildi, 82 yeni özellik eklendi. 141 özellik.

---

## 7. İşin sonunda elde edeceklerin

**Ürün olarak:** Üç katmanı tek doğruluk kaynağından türeten, piyasada eşi olmayan bir platform. Supabase'in veri katmanı, Retool'un uygulama katmanı ve dbdiagram'ın tasarım katmanı — hepsi bir arada ve senkron. Bu kombinasyonu bugün hiçbir ürün sunmuyor; kontrol ettim.

**Teknik olarak kanıtlanabilir iddialar:** "Altı veritabanı motorunda, her gece 275 gerçek veritabanına karşı doğrulanmış DDL üretiyoruz." "AI kalitemizin eval skoru 0.91." Bunlar pazarlama sloganı değil, ölçülmüş rakamlar — ve rakiplerinin hiçbirinde yok. Teknik kitleye satarken bu tür somut iddialar sloganlardan çok daha güçlü.

**İş olarak:** Gerçekçi bir senaryoda 12. ayda 180 Pro ve 20 Team müşterisiyle aylık 8.000 dolar, 24. ayda yaklaşık 520 bin dolar yıllık gelir. Brüt marj %76-83 bandında, ki bu SaaS için sağlıklı. Team planı koltuk bazlı olduğu için gelir müşterinin ekibi büyüdükçe kendiliğinden artıyor — genişleme geliri diye buna denir ve en değerli gelir türüdür.

**Terk edilemezlik:** Kullanıcının verisi platformda, ekibi panelde, iş akışı GitHub botunda. Değiştirme maliyeti yüksek. Bu, bugünkü halinde tamamen yok olan şey ve bir SaaS'ın var olma sebebi.

**Türkiye pazarında benzersiz bir konum:** Rakiplerin hiçbiri Türkçe konuşmuyor, KVKK'ya net cevap vermiyor, .NET/SQL Server odaklı değil ve Türkiye'de veri tutmuyor. Sen dördünü de yapabilirsin. Kurucunun burada olması dezavantaj değil, satış hendeği.

**Kendi adına:** Bunu bitirdiğinde elinde çok kiracılı bir SaaS platformu, dağıtık sistem, veritabanı iç yapıları, derleyici tasarımı, CRDT, Kubernetes ve AI değerlendirme sistemleri konularında gerçek deneyim olacak. Bu, sonuç ne olursa olsun kaybetmeyeceğin bir şey.

---

## 8. Bunun çalışmama ihtimali ve o zaman ne yapacağız

Dürüst olalım: bu planın tamamı **tek bir varsayıma** dayanıyor — *"Console retention yaratır."* Yani kullanıcıların yönetim panelini gerçekten günlük olarak kullanacağı, ekiplerini oraya davet edeceği varsayımı.

Bu varsayım yanlış olabilir. Onun için Faz 3'ün sonuna (yaklaşık 34. hafta) bir karar kapısı koydum. O noktada bakacağımız şey: proje başına günlük aktif Console kullanıcısı ikinin üstünde mi, ekip üyesi davet oranı %20'nin üstünde mi. Değilse plan değişiyor.

Plan B: migration güvenliği ve legacy veritabanı yönetimine yönelmek. Yani "eski, kimsenin bilmediği kurumsal veritabanını anla, belgele, güvenle değiştir" işi. Daha az müşteri ama daha büyük sözleşmeler. Bytebase ve Atlas'ın oynadığı alan ve orada tasarım katmanına sahip kimse yok.

Ayrıca bu varsayımı 34. haftaya kadar beklemeden test edeceğiz. Faz 2 biter bitmez, kaba bir Console prototipini yirmi kullanıcıya göstereceğiz. Erken sinyal, geç doğrulamadan iyidir.

Bir de en gerçekçi risk: **zaman.** Bu plan yaklaşık 1.800 saatlik iş. Haftada 35 saatle 52 hafta eder ama gerçekte beklenmeyen işler, destek, öğrenme eğrisi ile 16-18 aya çıkar. Bu yüzden önerim net: **Faz 3'ün sonunda (v2.0) dur, üç ay sadece sat ve öğren, sonra devam et.** Faz 4 ve 5, gelir gelmeye başladıktan sonra yapılacak işler.

Ve en yaygın ölüm sebebi teknik değil: tükenmişlik. Haftada kırk saati aşma, haftada bir tam gün izin al, her fazın sonunda yayınla ki geri bildirim ve motivasyon al. İlk beş ödeyen müşteri, dünyadaki en güçlü motivasyon kaynağıdır — oraya hızlı gitmeye çalış.

---

## 9. Şimdi ne yapacaksın

Kod yazmadan önce, bu hafta içinde halledilecek üç şey var:

**Bir:** `C:\Users\Enes Yel` dizininin kendisi bir git deposu ve remote'u başka bir projeye (`automated-recruitment-pipeline`) bağlı. Orada `git add -A` çalıştırırsan bütün ev klasörünü commit'lemeye çalışır. Bunu düzelt — ya o `.git` klasörünü kaldır ya da neden orada olduğunu anla.

**İki:** Ödeme altyapısını araştır. Stripe Türkiye'de sınırlı. Paddle veya LemonSqueezy gibi "merchant of record" sağlayıcıları vergiyi de üstleniyor. Bu, kod yazmadan çözülmesi gereken bir engel çünkü ürünü bitirip para alamamak en can sıkıcı senaryo.

**Üç:** `namines.com` alan adını al ve marka taraması yap.

Sonra kod tarafında ilk beş iş — bunlar etki/zorluk oranı en yüksek olanlar:

1. Sabit `ON DELETE CASCADE`'i kaldır, FK'ya gerçek bir davranış alanı ekle, MSSQL çoklu cascade yolu analizörünü yaz. *(Etki 10, Zorluk 3 — bugünkü ürünün en somut hatası bu.)*
2. `docker.sock` mount'unu kaldır. *(Etki 9, Zorluk 2 — bir satır konfigürasyon, kritik güvenlik açığı.)*
3. Serilog'u stdout'a al ve `Database.Migrate()`'i startup'tan çıkar. *(Etki 7, Zorluk 1 — bir saatlik iş.)*
4. Golden-file test altyapısını kur (Verify.Xunit). *(Etki 10, Zorluk 3 — bundan sonraki her değişiklik güvenli olur.)*
5. SignalR'a JWT auth ve Redis backplane ekle. *(Etki 9, Zorluk 3.)*

Bunlar bittiğinde index desteğine geç — o daha büyük bir iş ama artık test altyapın olduğu için güvenle yapabilirsin.

---

## 10. Her oturumda kendine soracakların

Bu belgeye her döndüğünde şu üç soruyu sor:

**"Bu hafta yaptığım iş, hangi kilometre taşına yaklaştırdı?"** Kilometre taşları: temel sağlam (hafta 6), tasarım lideri (hafta 14), ilk DB canlı (hafta 22), Console canlı (hafta 34). Hiçbirine yaklaşmayan iş, muhtemelen yapılmamalıydı.

**"Bu hafta neyi kestim?"** Faz 1'in hatası kapsam patlamasıydı — iki ayda 21 controller ama sıfır test ve index yok. Her hafta bir şey kesmiyorsan, kapsam yine büyüyor demektir.

**"Kullanıcı bunu ne zaman görecek?"** Yayınlanmayan iş, yapılmamış iştir. Her fazın sonunda yayınla.

---

## Referans dosyalar

Detaya inmen gerektiğinde:

Strateji için `00-VISION`, `01-MARKET`, `02-PRODUCT-SCOPE`.
Mimari için `03-ARCHITECTURE`, `04-NSL-SCHEMA-IR` (en önemlisi), `05` ile `12` arası.
Uygulamaya geçerken `15-PACKAGES` (tüm paketler ve sürümleri), `16-API-SURFACE` (her endpoint), `17-DIRECTORY-STRUCTURE` (hedef klasör yapısı ve Faz 1 dosyalarının nereye gideceği), `18-CONTROL-PLANE-DDL` (çalıştırılabilir tam şema), `19-ENV-VARS` (her ortam değişkeni).
Kalite için `13-SECURITY`, `20-TESTING-EVALS`, `21-OBSERVABILITY`.
İş için `22-BUSINESS-MODEL`, `23-GTM`, `24-ROADMAP`, `25-RISKS`.
Terim bilmediğinde `26-GLOSSARY`.

---

*Son bir not: Bugün elinde iki ayda yazılmış, çalışan, güvenlik bilinci olan 37 bin satır kod var. Bu planın hiçbir yeri "baştan yaz" demiyor. Yapacağımız şey, o kodun üzerine eksik olan üç şeyi eklemek: ifade gücü, doğruluk kanıtı ve terk edilemezlik. Üçü de mühendislik işi ve üçü de yapılabilir.*
