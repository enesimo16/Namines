# 13 — Namines Desk: Durum ve Devam Planı

> Sıradan bir durum özeti — "bitti" değil, "şu an burada, sırada bunlar var" demek için.

---

## 1. Şu an ne var

**Desk çalışıyor ve gerçek bir veritabanına karşı canlı doğrulandı.**

- Kimlik: Namines hesabıyla giriş (JWT) + ana uygulamadan tek tıkla SSO devri.
- Pano kabuğu: kalıcı sol gezinme (arama, proje seçici, gruplu menü, daralt/genişlet), üst şerit (ekmek kırıntısı, Namines/Destek, profil menüsü), açık/koyu tema, mobil çekmece.
- Şema (Canvas): salt-okunur harita, drift rozetleri, otomatik yerleşim.
- Veri: CRUD, filtre/sıralama/sayfalama, dışa aktarma, toplu silme (10+'da "SİL" onayı), FK açılır listesi.
- SQL konsolu: yalnızca Owner, yalnızca tek SELECT/WITH/EXPLAIN/SHOW, çok katmanlı güvenlik.
- Sürümler / Kayıtlar / Analitik / Ekip / API anahtarları: hepsi gerçek veriden, hepsi kendi sınırını (ne yapmadığını) ekranda söylüyor.
- 72 test (deterministik katman + regresyon), `tsc`/`next build` temiz.

Ayrıntı ve kanıtlar: [`11-DESK-V2-TAMAMLANDI.md`](11-DESK-V2-TAMAMLANDI.md), [`12-DESK-PANO-KABUGU.md`](12-DESK-PANO-KABUGU.md).

---

## 2. Bilinçli olarak eksik / sınırlı bırakılanlar

Bunlar unutulmadı — ya bir karara bağlı ya da sırası gelmedi:

| Ne | Neden şimdi değil |
|---|---|
| Toplu **güncelleme** (yalnızca toplu silme var) | Plan yalnızca silmeyi netleştirmişti |
| Log dışa aktarma (CSV) | Küçük, bağımsız bir ek — Data'nın export'uyla aynı desen |
| Uyarı kuralları (E5.3, "silme olursa e-posta at") | E-posta altyapısı henüz yok — kullanıcının açık talimatıyla erteli |
| Şemayı canlı veritabanına uygulama (DDL push) | Geri alınamaz; yedek (Vault) olmadan sorumsuzca olur |
| Component (React) testleri | Şu an yalnızca `lib/*` saf mantık test ediliyor |

---

## 3. Sıradaki geliştirme yönleri (öneri, öncelik sırasıyla)

1. **Analytics'e daha fazla derinlik** — periyot karşılaştırma, tabloya göre filtre.
2. **Log dışa aktarma** (yukarıdaki tablo).
3. **Component testleri** — özellikle `RowForm`, `BulkDeleteConfirm`, `AppShell`'in klavye/erişilebilirlik davranışı.
4. **Erişilebilirlik denetimi** — kabuk yeni; klavye-only gezinme ve ekran okuyucu ile bir tur henüz yapılmadı.
5. **Vault bitince:** Deployments ekranına "yedek al" kısayolu, restore öncesi otomatik yedek.
6. **Ground bitince:** Projects panosuna "yönetilen veritabanı oluştur" akışı (bugün yalnızca BYODB var).

Bunların hiçbiri şu an açık bir görev değil — bu liste bir sonraki oturumun başlangıç noktası.
