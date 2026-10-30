# 01 — Proje değerlendirmesi: Namines bugün nerede?

> Kaynak: 2026-10-29 Ekim denetimi (44 commit, ~87k satır, 79 düzeltme;
> bkz. [last-review/04-EKIM-DENETIMI.md](../last-review/04-EKIM-DENETIMI.md)),
> kod tabanının kendisi ve `docs/second-phase/18-EKOSISTEM-...` planı.
> Bu belge bir **görüş**; sayılar ölçülmüş, yorumlar benim.

---

## 1. Tek paragrafta

Namines teknik olarak **sıra dışı derecede derin** bir ürün: 6 motorlu DDL
derleyicisi, risk sınıflandırmalı Change Review, değişikliği gerçek motorda
kanıtlayan "Run Tests", şemadan otomatik REST (Gateway), yedek (Vault),
yönetilen DB (Ground), otomasyon (Flow), Hub, MCP + CLI. Test kültürü
güçlü (2500+ backend testi, golden-file DDL testleri). Ama son bir ayda
**genişlik derinliği geçti**: CI bir ay boyunca kırmızıydı, arayüzün
birçok yerinde sahte başarı ve uydurma veri vardı ve iki kritik güvenlik
açığı vardı (anonim OAuth girişi, korumasız MCP SQL). Denetimden sonra
temel sağlam. Yeni platformlara (masaüstü, mobil, eklenti) geçmeden önce
yapılması gereken şey özellik eklemek değil, **çekirdeği ürünleştirmek**.

---

## 2. Güçlü yanlar (gerçekten iyi olanlar)

| Alan | Neden önemli |
|---|---|
| **Change Review + risk katmanları + gerçek motorda kanıt** | Rakiplerin çoğu "şema üret" der; "bu değişiklik canlıda ne kırar, kanıtla" diyen az. Ürünün **kama ucu** (wedge) burası. |
| **Kanonik IR (`ir.json`) + 19 eject hedefi** | Her yeni platform/entegrasyon aynı IR'ı konuşabilir: masaüstü, eklenti ve bağlayıcılar için hazır bir ortak dil. |
| **MCP sunucusu + CLI** | AI editörleri (Claude Code, Cursor) zaten dağıtım kanalı. Masaüstü uygulaması bu çekirdeği yerel ajan olarak yeniden kullanabilir (bkz. [03](03-MASAUSTU-TAURI.md)). |
| **Test ve doğrulama kültürü** | Golden-file + Testcontainers + gerçek-Docker testleri. CI artık bunları gerçekten koşturuyor. |
| **Güvenlik bilinci belgelenmiş** | SSRF guard, egress allowlist, PII maskeleme, denetim kaydı, sandbox sertleştirmesi. Mobil "güvenlik ayağı" bunun üstüne doğal oturur. |
| **Türkçe, gerekçe odaklı dokümantasyon** | Yeni bir ajan/oturum nerede kalındığını bulabiliyor. Bu nadir. |

## 3. Zayıf yanlar (dürüst liste)

1. **Ürün sayısı > ekip kapasitesi.** Studio, Compile, Review, Gateway,
   Desk, Vault, Ground, Launch, Flow, Hub, Admin, MCP, CLI, Bot: 14 yüzey,
   1-2 kişi. Her biri "çalışıyor", ama hiçbiri "bitmiş" değil. Denetimde
   bulunan sorunların çoğu bu yüzden çıktı: yeni yüzey eklenirken eskisinin
   doğrulaması atlanıyor.
2. **Sahte başarı alışkanlığı.** Fork, issue, export, admin sağlığı,
   feature flag kaydı, Desk sorgusu... Hata olunca UI başarı gösteriyordu.
   Denetimde temizlendi ama kural olarak yazılmalı (bkz. §5). Mobil
   güvenlik uygulamasında aynı alışkanlık **felaket** olur: "onaylandı"
   diyen ama onaylamayan bir güvenlik uygulaması olmaz.
3. **Konumlandırma bulanık.** `27-LIFECYCLE-PIVOT.md` "AI ile DB lifecycle'ını
   güvenle yönet" diyor; landing ise hâlâ "şema üret, 6 motor" anlatıyor.
   Üç yeni platform bu mesajı ya güçlendirecek ya daha da dağıtacak.
4. **Üretim altyapısı henüz "ürün" değil.** Tek Oracle VM, `sslip.io`
   adresi, `namines.com` henüz yok; refresh token, passkey, cihaz kaydı,
   push altyapısı yok. Masaüstü, mobil ve eklentinin **üçü de** bunlara ihtiyaç duyuyor.
5. **Ölçüm yok.** PostHog kurulu ama denetim anında çağrılmıyordu. Hangi
   modülün kullanıldığını bilmeden yeni platform seçmek kör uçuş.

## 4. Sonuç ve öneri: "üç platform" kararına nasıl bakmalı?

Üç platform fikri doğru, **ama sırası ve amacı önemli.** Her biri tek bir
işe hizmet etmeli ve o iş çekirdek kamayı (güvenli DB değişikliği) güçlendirmeli:

| Platform | Tek işi | Kamayı nasıl güçlendirir |
|---|---|---|
| **Masaüstü (Tauri v2)** | Web'in **ulaşamadığı** veritabanlarına (localhost, VPN, intranet) güvenli erişim | "Canlıya göndermeden önce yerelde kanıtla" akışı: yerel DB, yerel Docker |
| **Tarayıcı eklentisi** | Kullanıcının **zaten bulunduğu yerde** (GitHub PR, Supabase, Firebase konsolu) Namines'i göstermek | PR'da görsel şema farkı + risk; konsolda "bu değişikliği önce review'dan geçir" |
| **Mobil (güvenlik ayağı)** | Kritik işlemler için **ikinci anahtar**: onay, alarm, acil durum şalteri | Destructive bir değişiklik biyometrik + imzalı onay olmadan canlıya çıkamaz |

Bu üçünün **ortak önkoşulu** aynı backend temeli: OAuth 2.1 yetkilendirme
sunucusu, cihaz kaydı, refresh token, passkey, push, sürümlü genel API, TS
SDK. Bu yüzden ilk iş platform değil **[02 — Ortak temel](02-ORTAK-TEMEL.md)**.
Entegrasyonlar (Supabase, Firebase, ...) ise üç platformun da **içeriği**.
Bağlayıcı çerçevesi olmadan eklenti ve masaüstü boş kabuk kalır
(bkz. [06](06-ENTEGRASYON-MIMARISI.md), [07](07-ENTEGRASYON-KATALOGU.md)).

## 5. Yeni platformlara geçmeden önce kural olarak yazılacaklar

Denetimden çıkan dersler; her yeni yüzey için **kabul kriteri**:

1. **Hata gizlenmez.** Sunucu hatası kullanıcıya hata olarak gösterilir.
   Yerel taslak/örnek veri modu varsa açıkça etiketlenir.
2. **Uydurma sayı yok.** Veri yoksa "veri yok" yazar. 0, 142 yıldız ya da "100 sağlık" yazmaz.
3. **Yetki istemcide değil sunucuda.** İstemcideki `isDev`/rol yalnızca
   görüntü içindir; her uç kendi yetkisini kendisi kontrol eder.
4. **CI yeşil değilse merge yok.** Branch koruması `main`'de zorunlu
   status check'lerle açılmalı (şu an açık değil).
5. **Her yeni yüzeyin bir ölçümü olur.** Kullanım olayı tanımlanmadan sürüm çıkmaz.
6. **Güvenlik varsayılanı kapalıdır.** Yeni izin, yeni host, yeni scope en
   dar hâliyle başlar; genişletmek açık bir karardır.
