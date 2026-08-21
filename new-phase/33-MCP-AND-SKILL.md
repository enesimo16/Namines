# 33 — MCP Sunucusu + Claude Skill (geliştirme döngüsüne girmek)

> Namines'i "gidip açtığın bir web sitesi" olmaktan çıkarıp, geliştiricinin **AI ile
> kod yazdığı anın içine** yerleştirme planı. [25-RISKS.md](25-RISKS.md) R4'ün
> azaltma maddesinin ("MCP sunucusu yayınla: LLM'ler Namines'i araç olarak
> kullansın — rakip değil, dağıtım kanalı") somut tasarımı.

---

## 1. Çözülen gerçek problem

Geliştirici Claude Code/Cursor ile hızla ilerliyor: endpoint'ler, ekranlar, iş
mantığı büyüyor. **Veritabanı yönetimi bu hıza yetişemiyor** — şema sürükleniyor,
migration'lar elle yazılıyor, "bu ALTER production'da ne kırar" sorusunun cevabını
kimse bilmiyor. AI kod üretimini hızlandırdıkça, DB tarafındaki bu boşluk
*büyüyor*.

Namines'in cevabı hazır (G8-G16): deterministik etki analizi, gerçek motorda
kanıt, onay akışı. Eksik olan tek şey **oraya ulaşmak** — geliştirici akışını
bırakıp tarayıcıya geçmek zorunda kalıyor.

---

## 2. Neden web app bu işi YAPAMAZ (mimari kısıt, tercih değil)

Senaryonun kalbi geliştiricinin **yerel dev veritabanı**: `localhost:5432`.

`Namines.Core/Security/SsrfGuard.cs` özel/ayrılmış adresleri reddeder ve bu
**bilinçlidir** — barındırılan bir servisin müşteri ağına veya cloud metadata
uçlarına (`169.254.169.254`) ulaşmasını engeller ([13-SECURITY.md](13-SECURITY.md)).
G14'te Data Explorer'ı yerelde test edebilmek için `IDbHostAccessPolicy` ile
**yalnızca Development'ta** geçerli bir istisna eklemek zorunda kaldık.

Sonuç net: **barındırılan Namines, kullanıcının localhost DB'sine hiçbir zaman
ulaşamaz.** Ulaşabilecek tek şey, kullanıcının kendi makinesinde çalışan bir
süreçtir → MCP sunucusu (stdio) veya CLI. Bu bir ürün tercihi değil, ağ ve
güvenlik gerçeğidir.

---

## 3. Satış cümlesi: "göster" değil, "kanıtla"

Dürüst olmak gerekirse Claude Code zaten `psql` ile şema okuyabiliyor ve
migration yazabiliyor. *"Claude DB'ni görsün"* satılabilir bir şey değil.

Namines'in eklediği, bir LLM'in **yapısal olarak** yapamadığı şey:

| Claude tek başına | Namines (mevcut, test edilmiş) |
|---|---|
| Migration yazar, doğru olduğunu **varsayar** | `BranchTestRunnerService` gerçek motor container'ında **çalıştırıp kanıtlar** |
| Riski tahmin eder (olasılıksal) | `SchemaImpactAnalyzer` **deterministik** hesaplar |
| Tek motor varsayar | 6 motor, golden-file + gerçek container doğrulamalı |
| Onay/iz kavramı yok | `ChangeRequest` + audit log + 1-kişi/2-kişi kuralı |

> **Konumlandırma:** MCP'nin vaadi *"Claude'un yazdığı migration'ı, çalıştırmadan
> önce kanıtlat."* Bu, [27 §5](27-LIFECYCLE-PIVOT.md)'teki kalıcı konumlandırma
> notunun ("agent'lar üretimi emtialaştırdı, emtialaştıramadıkları şey kanıt ve
> yönetişim") doğrudan uygulanmış hâli.

---

## 4. Mimari karar: MCP sunucusu .NET olacak ve Core'u GÖMECEK

İki seçenek vardı:

| | (a) Node/TS → HTTP → Namines backend | (b) .NET, `Namines.Core`/`Infrastructure` gömülü |
|---|---|---|
| localhost DB erişimi | ✅ (süreç yerelde) | ✅ |
| Auth gerekir mi | **Evet** — token yönetimi, sürtünme | **Hayır** |
| Backend ayakta olmalı mı | **Evet** | Hayır — offline çalışır |
| `run_tests` için Docker | Sunucunda → **senin maliyetin** | Kullanıcının kendi Docker'ı → **maliyet sıfır** |
| Kod tekrarı | Analiz mantığı HTTP ardında | 450 testlik kod **aynen** kullanılır |

**Seçim: (b).** Gerekçe yalnızca kolaylık değil — (a) SSRF sorununu geri getirir
(backend yine localhost'a ulaşamaz), auth sürtünmesi ekler ve `run_tests`
maliyetini senin sırtına yükler. (b)'de guard cloud'u korumaya devam eder; yerel
süreç zaten kullanıcının kendi makinesinde, kendi DB'sine bakıyor.

> Not: Bu, barındırılan ürünü **ikame etmez**. Ekip/onay/audit tarafı (G11, G16)
> sunucuda kalır. MCP tek kişilik geliştirme döngüsünü çözer; ChangeRequest akışı
> ekip işidir.

---

## 5. Araç yüzeyi (CLI ile aynı çekirdek)

[11-MIGRATIONS-BRANCHING.md §9](11-MIGRATIONS-BRANCHING.md)'da tanımlı CLI komutları
zaten bu yüzeyin kendisi. **MCP araçları CLI'ın üstüne kurulur, ayrı tasarlanmaz.**

### Faz 1 — ince dilim (3 araç, yeni iş mantığı YOK)

| Araç | Sarmaladığı mevcut servis | CLI karşılığı |
|---|---|---|
| `namines_pull_schema` | `DbIntrospectionService` | `namines pull` |
| `namines_analyze_impact` | `SchemaImpactAnalyzer` | `namines diff` |
| `namines_prove_migration` | `BranchTestRunnerService` | `namines apply --dry-run` |

Üçü de **var olan, test edilmiş** servisleri sarar. Bu fazda yazılan tek şey MCP
protokol yüzeyi.

### Faz 2 — üretim ve yazma

| Araç | Servis |
|---|---|
| `namines_generate_migration` | `MigrationService` |
| `namines_generate_ddl` | `IDdlGeneratorFactory` (6 motor) |
| `namines_open_change_request` | `ChangeRequestController` (sunucuya, auth ile) |

---

## 6. Skill mi MCP mi — ikisi, farklı katman

Rakip değiller; farklı soruları cevaplarlar:

- **MCP = yetenek.** Tipli, çağrılabilir araçlar. *"Ne yapabilirim?"*
- **Skill = yargı ve iş akışı.** Ne zaman analiz istenir, risk seviyeleri nasıl
  yorumlanır, ev kuralları nelerdir. *"Ne zaman ve nasıl?"*

Skill'in taşıyacağı kurallar (MCP'nin taşıyamayacağı, çünkü bunlar politika):

```
- Şema değişikliği öneriyorsan ÖNCE namines_analyze_impact çağır.
- OverallRisk = Destructive | Breaking ise: migration'ı UYGULAMA,
  bulguları kullanıcıya göster ve insan onayı iste.
- Risky ise: namines_prove_migration ile gerçek motorda kanıtla, sonra devam et.
- Motorun ham hata mesajını asla süsleme — olduğu gibi aktar (G5/G12 dersi).
```

---

## 7. Güvenlik ve geri-yazma modeli (en riskli parça)

Kullanıcının "değişikliği anında Claude'a kodlar" beklentisi ürünün en cazip ama
en tehlikeli parçası. Şema değişikliğini otomatik koda/DB'ye yansıtmak, yanlış
gittiğinde **sessizce veri kaybettirir**.

**Kural — Faz 1'de yazma yok:**

| İşlem | Faz 1 | Gerekçe |
|---|---|---|
| Şema okuma (`pull`) | ✅ | Salt-okunur, risksiz |
| Etki analizi | ✅ | Deterministik, yan etkisiz |
| Ephemeral container'da kanıt | ✅ | Tek kullanımlık, kullanıcının DB'sine dokunmaz |
| Migration **önerisi** üretme | ✅ | Metin döner, uygulanmaz |
| Kullanıcının DB'sine **uygulama** | ❌ | İnsan onayı olmadan asla |

[13-SECURITY.md](13-SECURITY.md)'nin "varsayılan asla veri kaybına doğru düşmez"
ilkesi ve `ReferentialActionSql`'deki aynı disiplin burada da geçerli.

**Ek notlar:**
- MCP sunucusu kullanıcının makinesinde çalıştığı için connection string ağdan
  geçmez — barındırılan üründeki "asla saklama" kuralından daha güçlü bir konum.
- `run_tests` kullanıcının Docker'ını kullanır; `docker.sock` **hiçbir container'a
  mount edilmez** (G1 kuralı, bkz. [CLAUDE.md](../CLAUDE.md)).

---

## 8. VS Code extension — neden şimdi değil

Kullanıcının gündeme getirdiği seçenek geçerli ama **sonraya**:

- MCP, Claude Code / Cursor / Zed / Windsurf'ün hepsinde tek implementasyonla çalışır
- Extension her IDE için ayrı bakım demektir
- Extension'ın da altında aynı çekirdeğe ihtiyacı var — yani MCP/CLI önce gelmeli

Extension, MCP kanıtlandıktan sonra **aynı çekirdeğin üstüne** bir UI katmanıdır.

---

## 9. Bu işe başlamadan bilinmesi gerekenler (dürüst kapsam notu)

Bu yeni bir **ürün yüzeyi** ve şu anki deploy engellerini çözmez. Bunlar duruyor:

- CI yok (`dotnet test` / `npm run build` hiçbir pipeline'da koşmuyor)
- Groq API anahtarı yok → AI'a bağlı özellikler sessizce boş dönüyor
- `Namines_Secure123!` üretilen developer package'lara sızıyor (`GroqAIService.cs`)

MCP işi bunları beklemeye alır. Karar bilinçli verilmeli.

---

## 10. İlişkili dokümanlar

- [11 §9](11-MIGRATIONS-BRANCHING.md) — CLI komut yüzeyi (MCP'nin çekirdeği)
- [25 R4](25-RISKS.md) — MCP'nin strateji gerekçesi
- [27 §5](27-LIFECYCLE-PIVOT.md) — "agent'ların emtialaştıramadığı şey" konumlandırması
- [28](28-IMPACT-ANALYSIS-ENGINE.md) — `analyze_impact`'in arkasındaki motor
- [29 §4](29-DATABASE-CHANGE-REVIEW.md) — `prove_migration`'ın arkasındaki "Run Tests"
- [13](13-SECURITY.md) — SSRF, connection string, yazma yolu kuralları
