# Erişilebilirlik — Namines

**Son güncelleme:** 10.09.2026 · İlgili denetim bulgusu: `A11Y-003`
(bkz. [13-accessibility-audit.md](audit/13-accessibility-audit.md))

---

## 1. Yapısal sınır: tuval klavyeyle kullanılamaz

Namines'in ana arayüzü `@xyflow/react` üzerine kurulu, sürükle-bırak bir şema
tuvali. Bu tür arayüzler **doğası gereği** klavye ve ekran okuyucu ile zor
kullanılır: tablo konumu fare koordinatıyla belirlenir, ilişkiler iki tutamağı
birbirine sürüklemekle kurulur.

Bunu "hata" olarak sunmuyoruz — ürün kararı. Ama karşılığı olmalı: **aynı işi
yapan, klavyeyle erişilebilir bir alternatif.** Bu belge o alternatifin ne
olduğunu, nereye kadar çalıştığını ve **nerede eksik kaldığını** yazıyor.

---

## 2. Erişilebilir alternatif: `.nsl` metin dili

Şemanın tamamı `.nsl` adlı düz metin dilinde tanımlanabilir. Dil şartnamesi:
[`new-phase/04-NSL-SCHEMA-IR.md`](../new-phase/04-NSL-SCHEMA-IR.md).

Metin olduğu için:

- Kullanıcının **kendi** editörüyle yazılır — ekran okuyucu desteği, klavye
  kısayolları, yazı boyutu, kontrast ayarı hep o editörün sorumluluğu. Biz
  erişilebilir bir metin editörü yazmaya çalışmıyoruz; kullanıcının zaten
  erişilebilir hale getirdiği editörü kullanmasına izin veriyoruz.
- Git'te diff'lenir, ekran okuyucuyla satır satır okunur.
- Fare gerektirmez.

```nsl
nsl 1.0

project "shopfront" {
  engine postgres
}

table users {
  id     uuid         pk default(gen_random_uuid())
  email  varchar(255) not null

  unique (email) name: uq_users_email
}
```

---

## 3. Nereye kadar çalışıyor — ölçülmüş durum

| Yol | Durum | Kanıt |
|---|---|---|
| Tuvaldeki şemayı `.nsl` olarak dışa aktar | ✅ Çalışıyor | `EjectPanel` → "NSL schema file" hedefi; `NslGenerator.Target = "nsl"` |
| `.nsl` metnini şemaya geri çevir | ✅ Çalışıyor | `POST /api/compile/nsl/parse` — hata satır numarası döner |
| `.nsl` metnini doğrula (derlemeden) | ✅ Çalışıyor | `POST /api/compile/nsl/validate` |
| Gidiş-dönüş kayıpsız mı | ✅ 24 test | [`NslRoundTripTests.cs`](../backend/Namines.Tests/Nsl/NslRoundTripTests.cs) — `A_second_round_trip_changes_nothing`, `Stable_identity_survives`, `Writing_is_deterministic` |
| `.nsl` dosyasını **arayüzden** içe aktar | ❌ **YOK** | Arayüzde `nsl/parse` çağıran hiçbir yer yok (`grep -rn "nsl/parse" frontend/` → 0 sonuç) |

**Dürüst özet:** Gidiş-dönüş **API seviyesinde tamamdır ve testlidir**, ama
arayüzde `.nsl` yükleme düğmesi yoktur. Klavye kullanıcısı bugün şemasını
metinle yazabilir ve doğrulayabilir — ancak bunu yapmak için HTTP isteği
göndermesi gerekir, düğmeye basması yetmez.

Bu, uyum iddiasını **eksik** bırakır. "Erişilebilir alternatifimiz var" demek
için o alternatifin ürünün içinden ulaşılabilir olması gerekir.

---

## 4. Bugün klavyeyle şema tanımlama

Arayüz düğmesi gelene kadar çalışan yol:

1. Şemayı bir metin editöründe `schema.nsl` olarak yaz (§2'deki biçim).
2. Doğrula:

```bash
curl -X POST http://localhost:5000/api/compile/nsl/parse \
  -H "Content-Type: application/json" \
  -H "X-Namines-Request: 1" \
  -d "{\"text\": $(python -c "import json,sys;print(json.dumps(open('schema.nsl').read()))"), \"dbType\": \"PostgreSQL\"}"
```

Hata varsa yanıt `line` alanı taşır — hangi satırın bozuk olduğu söylenir,
"geçersiz NSL" denip bırakılmaz.

3. Dönen `schema` nesnesini derleme uçlarına (`/api/compile/sql` vb.) gönder.

---

## 5. Kapatılması gereken açık

| # | İş | Efor | Neden gerekli |
|---|---|---|---|
| 1 | `/compile` ekranına "`.nsl` içe aktar" alanı: `<textarea>` + dosya seçici → `nsl/parse` → tuvale yükle | S | Alternatifi ürünün içine sokar; uyum iddiasını tamamlar |
| 2 | Ayrıştırma hatasını satır numarasıyla `<textarea>` yanında göster, `aria-live="assertive"` ile | XS | Ekran okuyucu hatayı duyurmadan metin yolu kullanılamaz |
| 3 | Bu belgeye ürün içinden bağlantı (tuvalin boş durumundan) | XS | Belgelenmiş ama bulunamayan alternatif, olmayan alternatiftir |

Ayrıca hâlâ denetlenmemiş olanlar (`13-accessibility-audit.md` §"Denetlenmeyen
ama gerekli kontroller"): renk kontrastı (WCAG AA 4.5:1), tam klavye gezinmesi,
`:focus-visible` görünürlüğü, form etiketleri (`B-41` — 51 girdinin 5'inde
`htmlFor` var).

---

## 6. Uyum konumu

**WCAG 2.2 AA iddiası yoktur.** Ölçüm yapılmadan iddia edilmez. Bu belge
"erişilebilir alternatif tasarlanmıştır ve API seviyesinde çalışır, arayüz
seviyesinde eksiktir" der — daha fazlasını değil.
