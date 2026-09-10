# Mobil hedefi — ürün kararı

**Son güncelleme:** 10.09.2026 · Kaynak bulgu: `Senaryo 7`
(bkz. [14-user-scenarios.md](audit/14-user-scenarios.md)) · Backlog: `B-55`

> **Bu bir öneridir, onaylanmış bir karar değil.** Ürün sahibinin onayına
> kadar geçerli konum §2'deki "bugünkü durum"dur. Karar verildiğinde bu
> belgenin başına onay tarihi yazılmalı.

---

## 1. Karar önerisi

| Yüzey | Mobil hedefi | Gerekçe |
|---|---|---|
| **Namines (tuval)** | **Desteklenmiyor.** Küçük ekranda açık bir uyarı + "masaüstünde aç" yönlendirmesi | Sürükle-bırak şema tuvali fare koordinatına dayanıyor; ilişki kurmak iki tutamağı birbirine sürüklemekle oluyor. Dokunmayla bunu yapılabilir hâle getirmek yeni bir etkileşim modeli tasarlamak demek — "duyarlı yapmak" değil |
| **Namines Desk** | **Destekleniyor.** Tablo/satır görünümleri, kayıt düzenleme, loglar | Desk zaten liste + form arayüzü; mobilde doğal karşılığı var ve `globals.css` 900px/560px kırılımlarını zaten tanımlıyor |
| **Namines Vault** | Destekleniyor (Desk içinde) | Yedek listesi ve "geri yükle" bir liste + onay akışı |
| **Namines Ground** | Destekleniyor (Desk içinde) | Aynı |
| **Pazarlama sayfaları** (`/`, `/security`, `/privacy`, `/terms`) | Destekleniyor | Metin içerikli; mobil trafiğin ilk temas noktası |

**Tek cümle:** *"Şemayı masaüstünde tasarlarsın, veriyi telefondan yönetirsin."*

---

## 2. Bugünkü durum — ölçüldü (375×812, 10.09.2026)

Karar tahminle değil, tarayıcıda ölçülerek verildi.

| Ölçüm | Sonuç | Değerlendirme |
|---|---|---|
| Sayfa yatay kaydırma | **Yok** (`scrollWidth` 375 = viewport) | ✅ Düzen taşmıyor |
| Tuval render | **Çalışıyor** — 25 düğüm çizildi | 🟡 Görünüyor, ama kullanılabilir olması ayrı |
| Görünür düğme | 28 | — |
| **44×44 px altı dokunma hedefi** | **26 / 28** | ❌ Ana sorun bu |
| `hidden md:*` ile mobilde gizlenen öğe | 25 yer | 🟡 Kısmi uyum var, ama kural yazılı değil |
| Desk CSS kırılımları | 900px, 560px | ✅ Desk için niyet zaten var |

**Kritik bulgu:** Düzen mobilde bozulmuyor — asıl sorun **dokunma hedefi
boyutu**. 28 düğmenin 26'sı Apple HIG ve WCAG 2.5.5'in önerdiği 44×44 px'in
altında. Yani mobilde ekran "doğru görünüyor" ama parmakla **isabetli
kullanılamıyor**. Bu, "mobil çalışıyor" sanılmasının en kolay yolu.

---

## 3. Neden "her şeyi yarım yamalak duyarlı yapmak" seçilmedi

Denetimin kendi ifadesiyle: *"Mobilde canvas yok, Desk var" demek, her şeyi
yarım yamalak duyarlı yapmaktan iyidir.*

Somut gerekçeler:

1. **Yarım destek en kötü seçenek.** Tuval mobilde açılıyor ve düğümler
   görünüyor — kullanıcı çalıştığını sanıyor, sonra bir ilişki kuramıyor.
   "Desteklenmiyor" demek dürüst; "açılıyor ama olmuyor" dürüst değil.
2. **Maliyet tek taraflı değil.** Dokunmayla şema düzenleme; çoklu seçim,
   yakınlaştırma, tutamak sürükleme ve bağlam menüsü için ayrı bir etkileşim
   dili gerektirir. Bu bir kırılım noktası işi değil, ikinci bir arayüz.
3. **Değer asimetrik.** Şema tasarımı oturarak yapılan bir iş. Veriye bakmak,
   bir kaydı düzeltmek, yedeği kontrol etmek ise yoldayken yapılan iş — ve
   Desk tam olarak o.

---

## 4. Kararın gerektirdiği işler

Karar onaylanırsa yapılacaklar, öncelik sırasıyla:

| # | İş | Efor | Neden |
|---|---|---|---|
| 1 | Dokunma hedeflerini 44×44 px'e çıkar (**Desk ve pazarlama sayfalarında**) | M | Ölçülen tek gerçek engel. Desteklenen yüzeylerde şart |
| 2 | Tuval için küçük ekranda açık uyarı ekranı ("Şema düzenleme masaüstünde") | S | "Açılıyor ama olmuyor" durumunu bitirir |
| 3 | Desk'i gerçek cihazda dene (iOS Safari + Android Chrome) | S | Emülasyon `100vh`, güvenli alan ve klavye davranışını yansıtmıyor |
| 4 | Bu kararı `README` ve pazarlama sayfasına yaz | XS | Kullanıcı satın almadan önce bilmeli |

**Yapılmayacak:** Tuvali dokunmayla düzenlenebilir hâle getirmek. Bu karar
değişirse ayrı bir ürün kararı olarak yeniden ele alınır.

---

## 5. Bu belge neyi kanıtlamıyor

- **Gerçek cihazda denenmedi.** Ölçümler tarayıcı emülasyonunda (375×812).
  Emülasyon `100vh` hatalarını, iOS güvenli alanını ve sanal klavyenin
  düzeni itmesini göstermez.
- **Desk mobilde uçtan uca denenmedi** — oturum gerektirdiği için bu ölçümde
  yalnızca tuval yüzeyi test edildi. §4'ün 3. maddesi bu yüzden var.
- **Ekran okuyucu ile denenmedi** (mobil VoiceOver/TalkBack). Bkz. `B-42`.
