# 15 — AYRI ÜRÜN: Flow (veritabanı otomasyonları)

> ⚠️ **Bu Namines değil.** Ayrı ürün. Fikri kaybetmemek için yazıldı;
> **şimdi başlanacak diye değil.**

---

## Fikir

Veritabanı olaylarına bağlı otomasyonlar:

> *"`users` tablosuna yeni kayıt gelince SMTP ile hoş geldin maili at."*
> *"`orders.status` = 'shipped' olunca webhook tetikle."*

Basitten başlayıp genişleyen bir kural motoru.

## Değerlendirme: 🔴 Şimdi değil

Fikir kötü değil — **zamanlaması ve konumu** yanlış.

### 1. Namines'in farkıyla hiç ilgisi yok

Namines'in savunulabilir tarafı: *değişikliği production'a göndermeden önce
neyin kırılacağını kanıtlamak.* Otomasyon bunun neresinde? Hiçbir yerinde.
Ayrı bir problem, ayrı bir kullanıcı anı, ayrı bir satın alma sebebi.

### 2. Kalabalık ve olgun bir pazar

Zapier, n8n, Make, Windmill, Supabase'in kendi Database Webhooks'u, Postgres
`LISTEN/NOTIFY` + trigger. Buraya zayıf bir ürünle girmek dikkat çekmez.

### 3. Teknik yükü göründüğünden çok ağır

"Yeni kayıt gelince mail at" cümlesi basit; arkasındaki sistem değil:

- **Olay yakalama** — polling mi, trigger mı, CDC mi? Üçü de ayrı bir dünya
- **Kuyruk ve yeniden deneme** — SMTP düştü, ne olacak?
- **Teslim garantisi** — en az bir kez mi, tam olarak bir kez mi? İkincisi zor
- **Sıralama** — iki olay ters sırada işlenirse?
- **Zehirli mesaj** — sürekli hata veren bir kural sistemi kilitler
- **Sonsuz döngü** — otomasyon bir tabloya yazıyor, o da otomasyonu tetikliyor
- **Gizlilik** — SMTP şifresi, webhook sırrı: sır yönetimi altyapısı gerekiyor

Bunların hiçbiri "sonra ekleriz" değil; **ilk günden doğru olması gereken**
şeyler. Yanlış yapıldığında sonuç: müşterinin müşterisine iki kez mail gitmesi
ya da hiç gitmemesi.

### 4. Sürekli çalışan altyapı demek

Namines bugün **istek-yanıt**. Flow, 7/24 çalışan bir işçi katmanı, kuyruk ve
izleme demek. İşletme modeli değişiyor (bkz. [14](14-AYRI-URUN-DEVELOPMENT-HOSTING.md)).

## Ne zaman geri dönülür

- Namines'in ödeyen müşterileri **bunu kendileri isterse** (varsayımla değil,
  taleple)
- Ve o zaman bile: önce **en dar hâli** — tek olay türü (satır eklendi), tek
  eylem (webhook). Mail, SMTP, şablon, koşul mantığı sonra.

Webhook'la başlamak doğru olan: teslim sorumluluğunun çoğunu karşı tarafa
bırakır, sır yönetimi minimumdur.

## 🔴 Yapılmayacak (şimdilik)

- Kuyruk/işçi altyapısı kurmak
- SMTP kimlik bilgisi saklamak — sır yönetimi ayrı bir sorumluluk sınıfı
- Kullanıcının veritabanına **trigger yazmak.** Namines'in bugünkü güven
  modeli "senin veritabanına yazmam, sadece kanıtlarım" üzerine kurulu;
  trigger kurmak bu sınırı geçer ve tüm konumlandırmayı zayıflatır
