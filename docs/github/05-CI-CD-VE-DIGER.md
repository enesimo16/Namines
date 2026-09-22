# 05 — CI/CD ve GitHub'ın Diğer Kullanım Yerleri

> **Sıra: 5.** Buradaki işlerin çoğu küçük ama ürünün GitHub'daki
> görünürlüğünü belirleyen şeyler.

## 1. CLI + GitHub Action

Komut yüzeyi `new-phase/11 §9`'da zaten tanımlı — burada yeniden tasarlanmıyor,
yayınlanıyor.

```yaml
- uses: namines/setup-action@v1
- run: npx namines validate .namines/schema.nsl
- run: npx namines diff --from origin/main --to HEAD --fail-on destructive
- run: npx namines drift --env production      # drift varsa exit 1
```

Bağımlılık: npm hesabı ve yayın (34 §2). Action ayrı bir public depo
(`namines/setup-action`) ister.

**Bot ile Action arasındaki iş bölümü:** Bot, kullanıcının hiçbir şey
kurmadığı depolarda çalışır (App yeter). Action, kendi CI'ını kontrol eden
ekipler için — `--fail-on destructive` ile merge'i kendi pipeline'larında
bloke ederler. İkisi aynı analizörü kullanır, aynı sonucu verir.

## 2. `namines/schema-review` required check

Depo ayarlarında bu check "required" yapılırsa yıkıcı şema değişikliği merge
edilemez. **Ürünün en kısa değer cümlesi bu** — kurulum tek bir onay kutusu.
Dokümantasyonda ve onboarding'de öne çıkarılmalı.

## 3. GitHub ile giriş

OAuth ile "Sign in with GitHub". Depo bağlama zaten GitHub kimliği gerektiriyor;
girişin de aynı kimlikle olması, kullanıcının iki ayrı hesap ilişkisi
kurmasını gereksiz kılıyor.

> **Dikkat:** OAuth App ile GitHub App **ayrı** şeyler. Giriş için OAuth,
> depo erişimi için App kurulumu. İkisi karıştırılırsa "giriş yaptım ama
> depolarımı göremiyor" şikâyeti çıkar — ekranda ayrı ayrı anlatılmalı.

## 4. Eject çıktısı → yeni depo

19 eject hedefi var ve çıktı bugün indiriliyor. İzin `pull-request` veya
`push` ise: **"Bunu yeni bir GitHub deposu olarak oluştur"** — `namines`
organizasyonunda değil, kullanıcının kendi hesabında/org'unda.

Değeri: üretilen yönetim paneli + SDK + migration'lar tek tıkla çalışan bir
depo hâline gelir. Namines'in çıktısı bir zip değil, bir proje olur.

## 5. Drift → issue

Zamanlanmış drift kontrolü (repo'daki şema ↔ canlı DB) fark bulursa depoda
issue açar: *"kod şunu diyor, veritabanında şu var, üç yerde ayrışmışlar."*

Issue **güncellenir**, her koşuda yenisi açılmaz — aynı drift için 30 issue,
özelliğin kapatılma sebebidir.

## 6. Sosyal / görünürlük

- Public share sayfasındaki badge snippet (`BadgeSnippet.tsx` zaten var) →
  README'ye yapıştırılabilir şema rozeti.
- Örnek depolar: her ayrıştırıcı için (Prisma / EF Core / Supabase) bir
  demo deposu. [01](01-DEPO-TARAMA.md)'in doğrulama listesi bunlarla örtüşüyor —
  aynı depolar hem test hem vitrin.

## Sıralama notu

Bu bölümdeki işler birbirine bağlı değil; tek tek alınabilir. Ama **2 numara
(required check) en yüksek değer/maliyet oranına sahip olan** ve
[02](02-BOT-VE-PREVIEW-DB.md) biter bitmez hazır hâle geliyor.
