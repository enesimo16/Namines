# 10 — Namines Desk v2 Yol Haritası

> **Neden "v1.1" değil "v2":** Namines (ana ürün) şu an kendi v2 sürümünde.
> Desk'in bir sonraki büyük sürümünü "v1.1" diye adlandırmak iki ayrı versiyon
> şeması yaratıp kafa karıştırırdı. Bundan sonra: **Desk v1** = D1-D7 (bitti,
> `09-YOL-HARITASI.md`), **Desk v2** = bu doküman.
>
> Kapsam: v1 dokümanlarının "kapsam dışı (v1)" bölümlerinde biriken her şey —
> hem eskiden "v1.1" etiketli üç madde, hem ekran bazlı ertelenenler. Hiçbiri
> yarım bırakılmış iş değil, her biri kendi dokümanında bilinçli olarak
> sonraya bırakılmıştı; burada tek listede toplanıyor.

---

## Öneri sırası

```
E1  Kimlik'in kalanı        — API anahtarı yönetimi + SSO devri (D1'in üzerine)
E2  Projects'in kalanı      — takım/üye yönetimi, arama/filtreleme
E3  Canvas'ın kalanı        — minimap/otomatik yerleşim
E4  Data'nın kalanı         — toplu işlem, ham SQL konsolu
E5  Logs'un kalanı          — canlı akış, dışa aktarma, uyarı kuralları
────────────────────────────
Kalıcı olarak Desk'e GİRMEYECEKLER (ayrı bölüm, aşağıda)
```

**Neden bu sıra:** E1 (kimlik) her ekranın altyapısını genişletiyor, en ucuz ve
en temel olan önce gelir. E2-E5 birbirinden bağımsız, D-serisindeki bağımlılık
zincirinden farklı olarak paralel de yapılabilir — sıra yalnızca bir öneri.

---

## E1 — Kimlik'in kalanı (`01-KIMLIK-VE-OTURUM.md`'nin v1 dışı bıraktıkları)

### E1.1 API anahtarı yönetim ekranı

**Bugünkü durum:** Desk anahtar kavramını hiç göstermiyor (D1'in kendi kararı);
anahtarlar yalnızca ana uygulamada `GatewayKeyController` üzerinden yönetiliyor.

**v2'de eklenecek:** Desk'e bir "API Anahtarları" ekranı — proje sahipleri için,
dış uygulamaların kullandığı anahtarları görüntüleme/oluşturma/iptal etme.
Backend **zaten tamamen hazır** (`POST/GET/DELETE /api/gateway/keys/*`,
Admin-ve-üstü yetkili) — bu tamamen bir arayüz işi.

**Kabul kriteri:** Yeni anahtar üret → yalnızca bir kez ham değer göster →
sonraki girişte yalnızca önek + oluşturulma tarihi görünsün (mevcut
`GatewayApiKey.KeyHash` tasarımıyla tutarlı, ham değer hiçbir yerde saklanmıyor).

### E1.2 SSO devri (ana uygulamadan tek tıkla geçiş)

**Bugünkü durum:** Kullanıcı Desk'e her girişte e-posta+parola giriyor (D1).

**v2'de eklenecek:** Ana uygulamadaki "Namines Desk" bağlantısı tek kullanımlık,
kısa ömürlü (saniyeler) bir devir jetonu taşır; Desk bunu JWT'ye çevirir.

**Gereken backend (yeni):** `POST /api/auth/desk-handoff-token` (ana uygulamada,
oturumlu kullanıcı için tek kullanımlık jeton üretir, TTL ≤30 sn, tek kullanımlık
— kullanıldıktan sonra veya süresi dolunca geçersiz) + Desk tarafında
`POST /api/auth/desk-handoff-exchange?token=...` (jetonu tam JWT'ye çevirir).

> ⚠️ Jeton URL'de taşınacaksa (yönlendirme linki) tarayıcı geçmişine ve
> `Referer` başlığına düşer — bu yüzden tek kullanımlık + saniyeler ömürlü
> şart (01-KIMLIK-VE-OTURUM.md'nin kendi uyarısı).

**Karar bekliyor:** jetonun taşınma şekli (query param mı, kısa ömürlü bir
`POST` formu mu) — bkz. `34-SENDEN-BEKLENENLER.md` madde 11.

---

## E2 — Projects'in kalanı (`02-PROJECTS.md` §5)

### E2.1 Takım/üye yönetimi

**Bugünkü durum:** `ProjectMemberController` ana uygulamada var; Desk'e hiç
bağlı değil.

**v2'de eklenecek:** Desk'te proje ayarları altında salt-görüntüleme bir üye
listesi (kim hangi rolde) — davet/rol-değiştirme YOK (bu hâlâ ana uygulamanın
işi, iki yerde aynı işi yapmak "hangi ekran yetkili" sorusunu bulanıklaştırır,
tıpkı D5'in onay ekranını Desk'e taşımama kararıyla aynı gerekçe).

### E2.2 Arama/filtreleme (proje kartları)

**Bugünkü durum:** `Projects.tsx` düz bir kart ızgarası (D2), 5-10 projede yeterli.

**v2'de eklenecek:** Proje sayısı arttıkça (eşik: 20+) ad/motor/bağlantı-durumu
üzerinden istemci-taraflı filtre. Backend değişikliği gerekmiyor — `/api/auth/
projects` zaten tüm alanları döndürüyor (D2).

---

## E3 — Canvas'ın kalanı (`03-CANVAS.md` §5)

### E3.1 Minimap / otomatik yerleşim düğmesi

**Bugünkü durum:** `Canvas.tsx` basit bir ızgara yerleşimi kullanıyor
(tasarımda konumu olmayan tablolar için), `@xyflow/react`'in kendi `MiniMap`'i
zaten Canvas'ta var ama otomatik-yeniden-düzenle düğmesi yok.

**v2'de eklenecek:** 30+ tablolu şemalarda "Otomatik düzenle" düğmesi —
`@dagrejs/dagre`'ı Desk'in **kendi** bağımlılığı olarak ekler (ana uygulamanın
`lib/autoLayout.ts`'i KOPYALANMAZ — mikroservis sınırı, third-phase §5).
Yalnızca hayalet/konumsuz düğümleri değil, TÜM görünümü yeniden düzenler; bu
yüzden konum yine KAYDEDİLMEZ (§4'ün "Desk hiç kaydetmiyor" kararı korunuyor).

**Not:** Şema düzenleme ve konum kaydetme kalıcı olarak Desk'e girmeyecek —
aşağıdaki "Kalıcı olarak dışarıda" bölümüne bakın.

---

## E4 — Data'nın kalanı (`04-DATA-CRUD.md` §3)

### E4.1 Toplu düzenleme / silme

**Bugünkü durum:** D4'te tekil satır ekle/güncelle/sil var; toplu işlem yok.

**v2'de eklenecek:** Satır seçme (checkbox sütunu) + "N satırı sil" / "N satırda
şu kolonu güncelle". Backend **kısmen hazır**: `POST /api/gateway/import` toplu
ekleme için var; toplu güncelleme/silme ucu YOK, eklenmesi gerekiyor
(`GatewayBulkUpdateRequest`/`GatewayBulkDeleteRequest` — tek işlem, ya hepsi ya
hiçbiri, `GatewayService.ImportAsync`'teki transaction deseniyle aynı).

**Karar bekliyor:** toplu silmenin geri-alınamaz olması nedeniyle bir onay eşiği
(ör. "N satırdan fazlasını silmek için yaz: SİL") — bkz. `34-SENDEN-BEKLENENLER.md`
madde 12.

### E4.2 Ham SQL konsolu

**Bugünkü durum:** `POST /api/gateway/query` var ama `CanExecuteSql` ayrı bir
API-anahtarı yetkisi (08 §1) ve bugün yalnızca dış uygulamalar için açık;
Desk'in oturum yolu bu yetkiyi hiç kontrol etmiyor.

**v2'de eklenecek:** Desk'te salt-okunur bir SQL editörü — ama **yalnızca proje
Owner'ı için**, ayrı bir onay adımıyla (mevcut `/query`'nin `readOnly` varsayılanı
korunur, `execute:true` iki adımlı bir "önce göster, sonra çalıştır" akışına
bağlanır — D5'teki "onay ayrı ekranın işi" ilkesiyle aynı temkin).

**Karar bekliyor:** Desk'in oturum yolunda ham SQL'e izin verilmesi güvenlik
açısından yeni bir yüzey açıyor — bu, Desk'in kendi başına alacağı bir karar
değil. Bkz. `34-SENDEN-BEKLENENLER.md` madde 13.

---

## E5 — Logs'un kalanı (`06-LOGS.md` §5)

### E5.1 Canlı akış ("Live" düğmesi)

**Bugünkü durum:** D6 yoklama yok (denetim kaydı okuma sunmuyor, yalnızca
istek üzerine sayfa okuyor); D5'in "yeni sürüm" şeridi 30 sn'de bir yokluyor,
Logs ekranı hiç yoklamıyor.

**v2'de eklenecek:** SignalR'a bağlanmak yerine, D5'teki AYNI 30 sn'lik yoklama
deseni Logs'a da uygulanır (yeni yazma varsa liste başına otomatik eklenir).
Gerçek SignalR akışı ayrı, daha büyük bir iş (Gateway'in kendi hub'ı yok,
CanvasHub yalnızca canvas işbirliği için var) — v2'de bilinçli olarak yapılmıyor.

### E5.2 Log dışa aktarma

**Bugünkü durum:** `export` ucu Data ekranı için var (D4), audit için yok.

**v2'de eklenecek:** `GET /api/gateway/keys/{projectId}/audit/export?format=csv`
— D6'nın filtre parametrelerini (from/to/kinds/tableName/succeeded) aynen kabul
eder, aynı Admin-ve-üstü yetki. Küçük bir backend eki, D4'ün export'uyla aynı
desen.

### E5.3 Uyarı kuralları

**Bugünkü durum:** Yok — ayrı bir alt sistem (06 §5'in kendi notu).

**v2'de eklenecek:** "X tablosunda silme olursa e-posta at" gibi kurallar.

**Karar bekliyor:** e-posta altyapısı bugün yok (`34-SENDEN-BEKLENENLER.md`'nin
davet-e-postası maddesiyle aynı blokaj) — bu madde ondan önce anlamlı
çalışamaz. Bkz. `34-SENDEN-BEKLENENLER.md` madde 14.

---

## Kalıcı olarak Desk'e GİRMEYECEKLER

Bunlar "sonraya" değil, **"hayır"** listesinde — ilgili dokümanların kendi
gerekçesiyle:

- **Şema düzenleme** (Canvas) — ana uygulamanın işi; iki yerde şema düzenlemek
  iki ayrı doğruluk kaynağı yaratır (03-CANVAS.md §1).
- **Canvas'ta konum kaydetme** — Desk'in kendi düzeni, ana uygulamadaki düzenle
  çakışır; "hangisi doğru olurdu" sorusuna cevap yok (03-CANVAS.md §4).
- **Proje oluşturma/silme** (Projects) — ana uygulamanın işi (02-PROJECTS.md §5).
- **İlişkili kayıtları satır içinde gösterme** (Data `Expand`) — Canvas zaten
  ilişkiyi gösteriyor, öncelik düşük ve Gateway'in `expand`'i oturum yolunda
  zaten çalışmıyor (35-KALAN-BUYUK-ISLER.md §2 — durumsuzluk kararına bağlı).
- **Deployments'ta onay verme** — ayrı bir doğruluk kaynağı yaratmamak için
  ana uygulamada kalıyor (05-DEPLOYMENTS.md §3).
- **Şemayı canlı veritabanına uygulama (DDL push)** — bu E-serisinde de YOK;
  Vault'tan sonraki ayrı bir aşama (05-DEPLOYMENTS.md §4.3, `09-YOL-HARITASI.md`).

---

## Her adımda geçerli kurallar (D-serisiyle aynı)

1. Yürüyen iskelet önce — tek uçtan uca yol kanıtlanmadan ikinci özellik yok.
2. Kanıt = çalışan komut + görülen çıktı; veri değiştiren her kriter `psql` ile
   bağımsız doğrulanır.
3. Regresyon: her adımda `dotnet test` (şu an 1359) ve Desk `tsc`/`next build`
   temiz kalmalı.
4. Mikroservis sınırı: `Namines.Core`/`frontend`'e referans yok.
5. Uydurma veri yok; kaynağı olmayan kutu/sayı çizilmez.

---

## Bu planın açtığı yeni "senden beklenenler"

Aşağıdaki dört madde `new-phase/34-SENDEN-BEKLENENLER.md`'ye eklendi (11-14) —
kod tarafı bu kararlar gelmeden başlayamaz ya da yarım kalır:

11. SSO devir jetonunun taşınma şekli (E1.2)
12. Toplu silme onay eşiği (E4.1)
13. Desk'in oturum yolunda ham SQL'e izin verilmesi — güvenlik kararı (E4.2)
14. Uyarı kuralları için e-posta altyapısı (E5.3) — zaten bilinen bir blokajın
    (davet e-postası) ikinci bir tüketicisi, yeni bir blokaj değil
