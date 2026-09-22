# 02 — Bot'un Canlıya Çıkması ve Preview DB

> **Sıra: 2.** GitHub App gerektirir (34 §8). Kod büyük ölçüde hazır;
> eksik olan kimlik ve **preview DB halkası**.

## 1. App'in kurulması (senden beklenen)

GitHub → Settings → Developer settings → GitHub Apps → New GitHub App.

| Alan | Değer |
|---|---|
| Webhook URL | API'nin public adresi + `/api/github/webhook` |
| Webhook secret | `Github__WebhookSecret` |
| İzinler (bu kademe) | `contents: read`, `pull_requests: write`, `checks: write` |
| Olaylar | `pull_request`, `issue_comment`, `push` |

`Github__AppId`, `Github__PrivateKey` (indirilen `.pem` içeriği),
`Github__WebhookSecret` ortam değişkenlerine konur.

> **`contents: write` bu kademede İSTENMEZ.** Yazma izni
> [04](04-YAZMA-YOLU-VE-IZIN.md)'te, kullanıcının açık onayıyla ve ayrı bir
> kapsam olarak gelir. Baştan istenen geniş izin, kurulum ekranında
> kullanıcının "bu ne kadar şeye erişiyor" diye durup vazgeçtiği yerdir.

## 2. Depo bağlama akışı

1. Proje ayarlarında **"GitHub deposu bağla"** → App kurulum ekranı.
2. Dönüşte `installation_id` yakalanır, `ProjectRepository` kaydı yazılır.
3. `SchemaPath` tespiti: `.namines/schema.nsl` varsa o; yoksa
   [01](01-DEPO-TARAMA.md)'in tarayıcısı çalışıp aday gösterir.
4. Bot artık o depodaki PR'ları izler.

**Aynı depo birden çok projeye bağlanabilir mi:** Hayır. Bir depo ↔ bir proje.
Aksi hâlde bir PR'a iki farklı risk yorumu düşer ve hangisinin doğru olduğu
belirsizleşir.

## 3. Bugün çalışan bot akışı (hatırlatma)

`pull_request` olayı → `.nsl`'in base ve head hâli okunur →
`SchemaImpactAnalyzer` → `PullRequestReviewComposer` → PR yorumu +
`namines/schema-review` status check. Yıkıcı değişiklik varsa `conclusion:
failure` → required check ise merge bloke.

**Bot yeni bulgu üretmiyor** — kural motoru karar veriyor, dil modeli değil.
PR'daki tabloya bakıp merge kararı veren insan için bu fark belirleyici.

## 4. Preview DB — **zaten var, bağlanması gerekiyor**

> **Düzeltme (bu plan yazılırken kodu tarayınca çıktı):** "preview DB" sıfırdan
> yazılacak bir şey değil. `IBranchDatabaseProvisioner` +
> `BranchDatabaseProvisioner` + `BranchDatabase` + `DockerSweeperBackgroundService`
> (TTL süpürücü) + kota kontrolü (`ListOpenBranchIdsAsync`) **çalışıyor** ve
> `BranchController` üzerinden uçları da var:
>
> | Uç | Ne yapar |
> |---|---|
> | `POST /api/branch/{id}/database` | Branch için gerçek, bağlanılabilir DB ayağa kaldırır |
> | `GET /api/branch/{id}/database` | Varsa döner, yoksa null (container oluşturmaz) |
> | `POST /api/branch/{id}/database/seed` | Deterministik örnek veri (AI yok) |
> | `DELETE /api/branch/{id}/database` | Yok eder |
>
> Aynı branch için ikinci container açılmıyor, `docker.sock` mount edilmiyor,
> süresi dolanı süpürücü topluyor.
>
> **Eksik olan tek şey: Namines branch'i ↔ GitHub branch/PR eşlemesi ve
> webhook'un bu uçları tetiklemesi.** Yani bu bölüm "yeni özellik" değil,
> "var olan özelliğe tetikleyici".

### Yaşam döngüsü

| Olay | Yapılan |
|---|---|
| `pull_request.opened` | main'in şemasından boş DB provision → PR'ın migration'ları uygulanır → sonuç PR yorumuna |
| `pull_request.synchronize` | DB **yeniden kurulur** (artımlı uygulanmaz — yarı uygulanmış bir preview, hiç olmayandan kötü) |
| `pull_request.closed` | DB silinir |
| TTL dolması | DB silinir, PR yorumu "preview süresi doldu, `/namines preview` ile yenile" olarak güncellenir |

### PR yorumunda ne var

- Risk tablosu (mevcut `PullRequestReviewComposer`)
- Uygulanan DDL
- Kilit tahmini
- **Preview bağlantısı** — Desk linki (`services/desk`), ham bağlantı dizesi değil
- Rollback planı

### Sınırlar

- **Varsayılan: boş şema + mock veri** (Smart Seed). Prod verisi kopyalanmaz.
- Proje başına eşzamanlı preview DB kotası (36-KOTA-VE-AJAN).
- **Sıralı provision.** Paralel ağır container bu makinede WSL2 backend'ini
  çökertiyor (AGENTS.md). Kuyruk gerekiyor, paralellik değil.
- Provision başarısızsa PR yorumu **başarısızlığı söyler**; sessizce atlanmaz.

## 5. `/namines` komutları

Bugün tanınıyor, "henüz yok" diyor. Canlıya çıkan sıra:

| Komut | Ne yapar | Bağımlılık |
|---|---|---|
| `plan` | Migration planını PR'a yazar | ✅ hazır (analizör) |
| `preview` | Preview DB'yi (yeniden) kurar | §4 |
| `types` | `namines.types.ts` üretir ve PR'a ekler | [04](04-YAZMA-YOLU-VE-IZIN.md) (yazma izni) |
| `rollback-plan` | Geri alma DDL'ini yazar | ✅ hazır |
| `approve` | Yıkıcı değişikliği onaylar, check'i yeşile çevirir | Yetki kontrolü: yalnızca depoda write yetkisi olan kullanıcı |

> `approve` komutunda yetki kontrolü **komutun kendisi kadar önemli.** Yorum
> yazabilen herkes `approve` yazabilir; GitHub'ın `author_association` alanı
> (`OWNER`/`MEMBER`/`COLLABORATOR`) kontrol edilmeden bu komut merge
> korumasını herkese açık bir düğmeye çevirir.

## Doğrulama

Gerçek bir depoda, gerçek bir PR'da: yorum düştü mü, check göründü mü, yıkıcı
değişiklikte `failure` mı, preview DB'ye Desk üzerinden gerçekten bağlanılıyor
mu, PR kapanınca container gerçekten silindi mi (`docker ps -a` ile bağımsız
doğrula — API'nin sözüne güvenme).
