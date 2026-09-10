# 18 — Eklenmesi önerilen özellikler

**Kural:** Bir özellik önerilmeden önce beş soru cevaplanır. Cevaplayamadıklarım
**önerilmedi** — listenin sonundaki "önerilmiyor" bölümüne konuldu.

---

## F-01 — Sorgu geçmişi

1. **Hangi problem?** Kullanıcı 20 dakika önce çalıştırdığı sorguyu bulamıyor.
2. **Sıklık?** SQL konsolu kullanan herkes, her oturumda.
3. **Rakiplerde?** Bu kategorinin **standart** özelliği (⚠️ doğrulanmadı).
4. **Farklılaştırıyor mu?** Hayır — eksikliği **fark ettiriyor**.
5. **Maliyete değer mi?** Evet. Tablo + uç + liste ekranı.

**Güvenlik etkisi:** Sorgular hassas veri içerebilir (WHERE'de e-posta vb.).
Kullanıcıya özel saklanmalı, `PiiRedactionEnricher` mantığı düşünülmeli.
**Efor:** S · **Öncelik:** **P1**

### ✅ Yapıldı (10.09.2026)

`SqlQueryHistoryEntry` + `SqlWorkbenchService` + `GET/DELETE
/api/gateway/desk-sql/history`. Desk SQL konsolunda "Geçmiş" sekmesi.

**Neden `GatewayAuditEntry` yetmedi:** O tablo, "değerler saklanmıyor" ilkesi
gereği **SQL metnini bilerek saklamıyor**. Denetim kaydı için doğru karar, ama
"20 dakika önce çalıştırdığım sorguyu bulamıyorum" problemini çözemez. İki
tablonun amacı farklı: biri **hesap verebilirlik**, diğeri **kullanıcının
kendi belleği**.

**Gizlilik kararları — hepsi ölçülebilir:**

| Karar | Neden |
|---|---|
| Kayıt **yalnızca çalıştıran** kullanıcıya görünür; proje Owner'ı bile başkasının geçmişini göremez | Bir meslektaşın hangi müşteriyi aradığı, projeye sahip olmakla kazanılan bir bilgi değil |
| Kullanıcı geçmişini **silebilir**; denetim kaydı silinmez | Zorunlu olmayan hassas veriyi tutmakta ısrar etmek gereksiz risk |
| Kullanıcı+proje başına **100 kayıt**, her yazmada budanır | Sınırsız büyüyen tablo = sonsuza kadar biriken hassas metin. Arka plan işine bırakmak, iş çalışmadığında sınırın sessizce yok olması demekti |
| 8000 karakterden uzun SQL kırpılır ve **kırpıldığı söylenir** (`truncated`) | Sessizce kırpılmış bir sorguyu kopyalayıp çalıştıran kullanıcı, farklı bir sorgu çalıştırdığını fark etmezdi |
| Uçlar `DeskSql` ile **aynı kapılardan** geçer (Owner + `AllowDeskSql`) | Ayrı/gevşek bir kural, konsolu kapatmanın hiçbir şeyi kapatmadığı bir arka kapı açardı |

**Başarısız sorgu da kaydediliyor** — kullanıcının aradığı çoğu zaman tam odur.

**CANLI DOĞRULANDI** (gerçek API + gerçek PostgreSQL): başarılı sorgu (1 satır,
43 ms), var olmayan tablo (`42P01 … does not exist`) ve **engellenen `DELETE`
denemesi** ("Only SELECT/WITH/EXPLAIN/SHOW…") — üçü de geçmişte, en yenisi
üstte. Kimlik doğrulamasız çağrı 401, olmayan proje 404.
18 birim testi (gizlilik izolasyonu ve budama dahil).

---

## F-02 — Kaydedilmiş sorgular

1. **Problem?** Tekrarlanan sorgular her seferinde yeniden yazılıyor.
2. **Sıklık?** Haftalık; ekipte günlük.
3. **Rakiplerde?** Standart (⚠️ doğrulanmadı).
4. **Farklılaştırır mı?** Ekip paylaşımı eklenirse **evet** — ürün zaten
   organizasyon modeline sahip, rakiplerin çoğu tek kullanıcılı.
5. **Değer mi?** Evet, F-01 ile aynı altyapı.

**Efor:** S (F-01'den sonra) · **Öncelik:** P1

### ✅ Yapıldı (10.09.2026)

`SavedQuery` + `GET/POST/DELETE /api/gateway/desk-sql/saved`. Konsolda
"Kayıtlı" sekmesi ve "Sorguyu kaydet" düğmesi.

**Neden geçmişle aynı tabloda değil:** Fark bir alan değil, bir **söz**.
Geçmiş kendiliğinden birikir ve kendiliğinden silinir (100 kayıt); kaydedilmiş
sorgu kullanıcının "bunu sakla" dediği şeydir ve **silinmez**. İkisini tek
tabloda bir `IsSaved` bayrağıyla birleştirmek, temizlik mantığının bir gün o
bayrağı atlamasına ve kasten saklanan sorgunun kaybolmasına bir adım kalması
demekti.

**Aynı ad = güncelleme, hata değil.** Hata dönmek kullanıcıyı önce silmeye
zorlardı; "kaydet"e ikinci kez basmak en doğal düzeltme hareketidir. Garantiyi
veritabanı veriyor: `(UserId, ProjectId, Name)` unique.

**Silmede `UserId` de koşulda:** id'yi bilen biri başkasının kaydını silemez ve
"bulunamadı" ile "senin değil" arasında ayrım yapılmıyor — ikincisi o id'nin
var olduğunu doğrulardı.

**Paylaşım BİLEREK eklenmedi** — alan olarak da. Paylaşılan bir sorgu,
başkasının çalıştıracağı bir metin demek; kimin ne çalıştırabileceği kararı
Owner kapısına bağlı ve o kapıyı dolaylı açan bir özelliği ölçmeden eklemek
doğru değil. Kullanılmayan bir `IsShared` sütunu da "paylaşım var" izlenimi
verirdi.

**CANLI DOĞRULANDI:** kaydet → aynı adla tekrar (aynı `id`, güncellenmiş SQL)
→ liste (tek kayıt) → boş ad reddi (400) → sil (204) → tekrar sil (404).

---

## F-03 — Executor için denetim kaydı ve risk kapısı

Bu bir "özellik" değil, [SEC-001](06-security-audit.md#sec-001)'in çözümü. Ama
ürün açısından da satılabilir: *"Her SQL çalıştırması kayda geçer."*

1. **Problem?** "Kim ne çalıştırdı" sorusunun cevabı yok.
2. **Sıklık?** Olay anında — yani en kritik anda.
3. **Rakiplerde?** Bytebase'in çekirdek iddiası (⚠️ doğrulanmadı).
4. **Farklılaştırır mı?** Yokluğu **ürünün ana iddiasını çürütüyor**.
5. **Değer mi?** Zorunlu.

**Efor:** M · **Öncelik:** **P0**

---

## F-04 — Jeton iptali (`SecurityStamp` claim'i)

1. **Problem?** Çalınmış oturum kapatılamıyor.
2. **Sıklık?** Nadir — ama gerçekleştiğinde kritik.
3. **Rakiplerde?** Kurumsal ürünlerde standart.
4. **Farklılaştırır mı?** Hayır; **satış engelini kaldırır**.
5. **Değer mi?** Evet — ASP.NET Identity'nin `SecurityStamp`'i zaten var,
   yalnızca claim'e eklenip kontrol edilecek.

**Efor:** M · **Öncelik:** P1

---

## F-05 — Boş durumlar (Desk'in 7 ekranı)

1. **Problem?** İlk girişte kullanıcı boş tablo görüyor, ne yapacağını bilmiyor.
2. **Sıklık?** Her yeni kullanıcı, bir kez — ama o bir kez dönüşümü belirliyor.
3. **Rakiplerde?** Standart.
4. **Farklılaştırır mı?** Hayır; **terk oranını düşürür**.
5. **Değer mi?** Evet — ekran başına birkaç satır.

**Efor:** S · **Öncelik:** **P1** (yatırım/getiri oranı en yüksek madde)

---

## F-06 — Demo → hesap geçişinde işin korunması

1. **Problem?** `/demo`'da ikna olan kullanıcı, kayıt olunca sıfırdan başlıyor
   (⚠️ doğrulanmadı — teyit edilmeli).
2. **Sıklık?** Her dönüşüm denemesi.
3. **Rakiplerde?** İyi olanlarda var.
4. **Farklılaştırır mı?** Hayır; **hunideki en büyük deliği kapatır**.
5. **Değer mi?** Evet — `useSchemaStore` + `/api/auth/sync` altyapısı hazır.

**Efor:** M · **Öncelik:** P1

---

## F-07 — Kısmi (tek tablo) geri yükleme

1. **Problem?** Bir tabloyu kurtarmak için tüm veritabanını geri yüklemek gerekiyor.
2. **Sıklık?** Nadir ama gerçek felaket anında.
3. **Rakiplerde?** ⚠️ Doğrulanmadı.
4. **Farklılaştırır mı?** Vault'un mevcut gücünü **derinleştirir**.
5. **Değer mi?** Evet — `pg_restore` `-t` bayrağını zaten destekliyor
   (`-Fc` formatı bilinçli olarak bunun için seçilmiş, kodda yazılı).

**Efor:** M · **Öncelik:** P2

---

## F-08 — CSV/JSON dışa aktarma

1. **Problem?** Veriyi dışarı almak için başka bir araç gerekiyor.
2. **Sıklık?** Haftalık.
3. **Rakiplerde?** Standart.
4. **Farklılaştırır mı?** Hayır.
5. **Değer mi?** Evet — küçük iş, sık ihtiyaç.

**Güvenlik notu:** Dışa aktarma **ayrı bir izin** olmalı. Okuma yetkisi olan
herkesin tüm tabloyu indirebilmesi, veri sızıntısının en sessiz yoludur.
Gateway'in `CanRead`/`CanWrite` modeline `CanExport` eklenmeli.

**Efor:** M · **Öncelik:** P2

---

## F-09 — MFA (TOTP)

1. **Problem?** Parola tek savunma hattı.
2. **Sıklık?** Kurulum bir kez, koruma sürekli.
3. **Rakiplerde?** Kurumsalda zorunlu.
4. **Farklılaştırır mı?** Hayır; **satış engelini kaldırır**.
5. **Değer mi?** Kurumsal hedefleniyorsa evet. ASP.NET Identity TOTP sağlayıcısı
   zaten pakette.

**Efor:** M · **Öncelik:** P2 (kurumsal hedefte P1)

---

## F-10 — Supabase entegrasyonu (Neon'un yanına)

1. **Problem?** Kullanıcı Supabase kullanıyorsa Ground'a taşınmak istemiyor.
2. **Sıklık?** Kurulum bir kez.
3. **Rakiplerde?** Doğrudan karşılığı yok — çünkü rakipler **kendileri** o platform.
4. **Farklılaştırır mı?** **Evet.** "Platformunuzu değiştirmeyin, üstüne
   yönetişim koyun" konumlanması, Ground'la yarışmaya çalışmaktan çok daha
   savunulabilir.
5. **Değer mi?** Evet — `IDatabaseProvider` soyutlaması **zaten var**, Neon
   sağlayıcısı örnek teşkil ediyor.

**Efor:** M · **Öncelik:** P2 — ama **stratejik değeri en yüksek** madde.

---

## ÖNERİLMEYEN özellikler ve nedenleri

| Özellik | Neden önerilmiyor |
|---|---|
| **GraphQL API** | Beş sorunun 1. ve 2.'sine cevap yok. REST Gateway zaten var; GraphQL talebi ölçülmeden eklenirse bakım yükü olur. |
| **Gerçek zamanlı abonelik** | Supabase'in alanı; orada yarışmama kararıyla çelişir. |
| **Görsel sorgu oluşturucu** | Hedef kitle SQL biliyor. Bu özellik genellikle yapılır, kullanılmaz. |
| **Dark mode** | ⚠️ Zaten var olabilir (`ThemeToggle.tsx` görüldü). Doğrula, yoksa P3. |
| **PITR (nokta-zamanlı geri yükleme)** | Motor/sağlayıcı özelliği; Namines'in katman olarak sunması çok pahalı. Neon gibi sağlayıcılara **devret**. |
| **Kendi mobil uygulaması** | Kategoride talep yok. |
| **Daha fazla AI özelliği** | Mevcut AI özellikleri farklılaştırmıyor (bkz. 15). Yeni AI eklemek yerine **mevcut AI'ın etki açıklamasını derinleştir**. |

---

## Öneri sırası (getiri/maliyet)

1. **F-05** boş durumlar — S efor, doğrudan dönüşüm
2. **F-03** executor denetimi — P0, güvenlik + ürün iddiası
3. **F-01/F-02** sorgu geçmişi + kayıt — S efor, en görünür eksik
4. **F-06** demo→hesap taşıma — huni deliği
5. **F-04** jeton iptali — satış engeli
6. **F-10** Supabase — stratejik konumlanma
