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

---

## F-02 — Kaydedilmiş sorgular

1. **Problem?** Tekrarlanan sorgular her seferinde yeniden yazılıyor.
2. **Sıklık?** Haftalık; ekipte günlük.
3. **Rakiplerde?** Standart (⚠️ doğrulanmadı).
4. **Farklılaştırır mı?** Ekip paylaşımı eklenirse **evet** — ürün zaten
   organizasyon modeline sahip, rakiplerin çoğu tek kullanıcılı.
5. **Değer mi?** Evet, F-01 ile aynı altyapı.

**Efor:** S (F-01'den sonra) · **Öncelik:** P1

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
