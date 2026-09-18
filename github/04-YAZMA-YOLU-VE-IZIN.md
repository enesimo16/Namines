# 04 — Yazma Yolu ve İzin Modeli

> **Sıra: 4.** Karar: **PR açılır; kullanıcı açık bir izin onayı (buton)
> verdikten sonra doğrudan push da yapılabilir.**

## 1. İki ayrı sınır, karıştırılmamalı

| Sınır | Kural |
|---|---|
| **Depo** | Namines yazabilir — PR ile, izin verilirse doğrudan push ile |
| **Veritabanı** | Namines kullanıcının canlı/prod DB'sine **yazmaz** |

Depoya yazma izni ikinci sınırı gevşetmiyor. Üretilen migration'ı uygulayan
hâlâ kullanıcının CI'ı veya açık onaylı Change Review. `12-ENTEGRASYONLAR`'ın
güven modeli aynen duruyor: **Namines kanıtlar, uygulamayı kullanıcı yapar.**

## 2. İzin durumları

`ProjectRepository.WriteMode` üç değer alır:

| Değer | Ne yapabilir | Nasıl geçilir |
|---|---|---|
| `none` (varsayılan) | Hiçbir şey yazmaz. Yalnızca yorum + check. | — |
| `pull-request` | `namines/schema-<n>` branch'i açar, commit atar, PR açar. `main`'e dokunmaz. | Proje ayarlarında **"Namines bu depoya PR açabilsin"** butonu |
| `push` | Onaydan sonraki değişiklikleri hedef branch'e **doğrudan** push eder. | Ayrı bir buton + ikinci bir onay ekranı; hedef branch açıkça seçilir |

Her geçiş `WriteApprovedAt` / `WriteApprovedBy` ile kayda geçer ve
`ChangeRequestAuditLog`'a düşer. İzin **geri alınabilir**, geri alındığı an
bekleyen yazma işleri iptal olur.

GitHub App kapsamı da kademeli: `push` seçilene kadar `contents: write`
istenmez. İzin ekranında kullanıcı **hangi iznin neden istendiğini** görür.

## 3. `push` modunda bile PR'a düşen değişiklikler

İzin ne olursa olsun aşağıdakiler doğrudan push edilmez:

- **Yıkıcı (destructive) şema değişikliği** — tablo/kolon silme, tip daraltma,
  `NOT NULL` ekleme. Risk sınıflandırması `Safe` değilse PR.
- **Hedef branch korumalıysa** (branch protection) — zaten GitHub reddeder;
  biz bunu önceden tespit edip PR'a düşeriz, 403 alıp hata göstermek yerine.
- **Merge kuyruğunda bekleyen bir PR aynı tablolara dokunuyorsa**
  (bkz. [03](03-COKLU-GELISTIRICI-MERGE.md)).

> Bu bant, izni anlamsızlaştırmıyor. Günlük işin çoğu — tip dosyası
> güncellemesi, yeni kolon, dokümantasyon, `.nsl` senkronu — `Safe` sınıfında
> ve doğrudan gidiyor. Bloke edilen, geri alınamayan şey.

## 4. Ne yazılıyor

Canvas'taki bir şema değişikliği tek bir commit/PR'a dönüşür:

```
.namines/schema.nsl        güncel şema
.namines/ir.json           kanonik IR
migrations/<ts>_<ad>.sql   üretilen migration (+ rollback)
src/namines.types.ts       tip senkronu (istenirse)
```

PR gövdesi: risk tablosu, etkilenen tablolar, rollback planı, preview DB linki.
Yani PR'ı açan da inceleyen de aynı bilgiyi görüyor.

## 5. İstemci yüzeyi (yeni)

`IGithubClient`'a: `CreateBlobAsync`, `CreateTreeAsync`, `CreateCommitAsync`,
`UpdateRefAsync`, `CreatePullRequestAsync`, `GetBranchProtectionAsync`.

`UpdateRefAsync` **force olmadan** çağrılır. Force push, kullanıcının
commit'lerini sessizce yok eden tek işlem — izin verilmiş olması onu güvenli
yapmıyor.

## 6. Çakışma: biz yazarken başkası yazdıysa

Push, beklenen `sha` üzerine yapılır (optimistic concurrency). Ref bu arada
oynadıysa GitHub reddeder → **yeniden denenmez**, değişiklik PR'a düşürülür ve
kullanıcıya "depo bu arada değişti, PR olarak açtım" denir. Sessiz retry,
kaybolan değişikliklerin klasik kaynağıdır.

## ⚠️ Dikkat

- **İzin butonu bir güven olayıdır.** Ekranda ne yapılacağı, hangi branch'e,
  neyin hariç tutulduğu (§3) yazılı olmalı. "İzin ver" tek başına yetmez.
- **Bot'un commit'i kimin adına?** App kendi kimliğiyle commit atar
  (`namines[bot]`). Kullanıcının adına commit atılmaz — denetim kaydını
  yalanlar.
- **Denetim kaydı zorunlu.** Her yazma işlemi `ChangeRequestAuditLog`'a:
  kim tetikledi, hangi izinle, hangi commit'e dönüştü.

## 🔴 Yapılmayacak

- Force push.
- `main`'e yıkıcı değişikliği doğrudan push etmek.
- İzin verilmeden "hazırlık olsun diye" branch açmak.
- Kullanıcının kendi commit'lerini yeniden yazmak (rebase/amend).
