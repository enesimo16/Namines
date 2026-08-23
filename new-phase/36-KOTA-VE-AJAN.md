# 36 — Kota Modeli ve Şema Ajanı

> İki soruna verilen cevap. Birincisi para, ikincisi kalite:
> **AI ve kullanıcı kotalandırması** ile **ilk prompt'tan çalışan bir şemaya
> giden ajan hattı.**
>
> İkisi bir arada duruyor çünkü ajan hattının her turu bütçe harcıyor — kotayı
> düşünmeden ajan tasarlamak, kullanıcının parasını görünmez biçimde yakmak olurdu.

---

## Önce sade hâli

**Problem 1 neydi:** Ücretli kullanıcı da ücretsiz kullanıcı da **aynı** AI
hakkını alıyordu. Abonelik bilgisi veritabanında duruyordu ama hiçbir sınırı
etkilemiyordu — yani para ödeyen karşılığını almıyor, ödemeyen de kısıtlanmıyordu.

**Problem 2 neydi:** Kullanıcı "e-ticaret şeması yap" yazdığında **tek bir AI
çağrısı** yapılıyor ve model ne döndürdüyse ekrana basılıyordu. Model birincil
anahtarı unutabilir, var olmayan bir tabloya bağ kurabilir ya da o veritabanının
kabul etmeyeceği bir tip seçebilirdi — ve kullanıcı bunu ancak veritabanı
reddedince öğrenirdi.

**Şimdi ne oldu:**
- Her planın kendi hakkı var ve kullanıcı bunu ekranda görüyor.
- Şema üretimi artık üç adımlı: **üret → denetle → düzelt.** Denetimi yapan AI
  değil, kural motoru ve gerçek veritabanı derleyicileri.

---

## 1. Kota modeli

### Tek karar noktası

Plan → hak eşlemesi [`PlanQuotas`](../backend/Namines.Core/Analysis/PlanQuotas.cs)
içinde, **tek yerde**. Aynı sayının iki yerde yazılması bu kod tabanında zaten
bir kez gerçek hataya yol açtı (bkz. CHECKLIST G49): kotanın ikinci bir kopyası
token yerine çağrı sayıyor, paylaşılan havuza hiç dokunmuyor ve günü farklı bir
saat diliminde başlatıyordu.

| Plan | Günlük AI token | Gateway rpm (anahtar başına) | Branch DB | Ephemeral koşu |
|------|-----------------|------------------------------|-----------|----------------|
| Free | 20.000 | 60 | 0 | 3 |
| Pro | 200.000 | 600 | 2 | 20 |
| Team | 1.000.000 | 3.000 | 20 | sınırsız |
| Enterprise | 10.000.000 | 10.000 | sınırsız | sınırsız |

> ⚠️ **Bu sayılar geçici varsayılan.** Gerçek rakamlar ürün kararıdır ve
> [34](34-SENDEN-BEKLENENLER.md) §4'te bekliyor. Tek yerde durdukları için
> değiştirmek tek satır.

### Kararlar ve gerekçeleri

**Free bilerek "kullanılabilir ama dar".** Sıfır vermek ürünü denenemez kılar;
cömert vermek ücretliye geçme sebebini yok eder.

**Tanınmayan her abonelik durumu Free'ye düşer.** Ters yönde düşmek — bilinmeyen
bir durumu ücretli saymak — ödeme yapmamış birine ücretli kaynak açtırırdı ve bu,
faturayı büyütmekten başka işe yaramaz.

**İki ayrı kap var: kullanıcı tavanı + paylaşılan günlük havuz.** Kullanıcı
tavanı tek bir kişinin havuzu boşaltmasını engelliyor; havuz da toplam AI
harcamasını sınırlıyor. Kullanıcı `/api/quota/status`'ta **ikisini birden**
görüyor — kendi hakkı dolmadığı hâlde "AI şu an kısıtlı" cevabı almak, sebebi
görünmezse arıza gibi hissettirir.

**Kontrol ile harcama ayrı** (`CheckAsync` / `ConsumeAsync`). Bütçe yalnızca
BAŞARIDA harcanıyor: dış bir servisin arızasını kullanıcının günlük hakkından
kesmek yanlış olurdu. Peşin alıp geri vermek de denendi ve bırakıldı — iadenin
kendisi de başarısız olabildiği için ikinci bir arıza yolu açıyordu.

**Gateway anahtarı planın tavanını aşamıyor.** Aksi hâlde ücretsiz bir hesap
kendine 100.000 rpm'lik bir anahtar üretip planı anlamsız kılardı ve bunu fark
etmenin tek yolu faturaya bakmak olurdu.

**Tavan her okumada plana göre düzeltiliyor.** Plan değişince (yükseltme ya da
iptal) kullanıcının satırı kendiliğinden doğru sınıra gelir; ayrı bir "planı
senkronla" işi gerekmiyor ve unutulamıyor.

---

## 2. Şema ajanı

### Hattın şekli

```
kullanıcının cümlesi
      ↓
  [AI] taslak şema
      ↓
  [DETERMİNİSTİK KAPI]
    · LinterService     → kural ihlalleri (yalnızca HATA)
    · DDL üreticisi     → hedef motorda gerçekten derleniyor mu?
      ↓
  bulgu var mı? ──hayır──→ bitti
      │ evet
      ↓
  [AI] "şu somut şeyler yanlış, düzelt"
      ↓
  tekrar denetle · tur sınırına kadar
      ↓
  kalan bulgular AÇIKÇA raporlanır
```

### Kararlar ve gerekçeleri

**Kapı deterministik, ikinci bir model değil.** "Modele kendi çıktısını kontrol
ettirmek" aynı yanılgıyı iki kez üretir. Linter ve DDL üreticisi ise aynı girdiye
her zaman aynı cevabı verir. Bu, kod tabanının geri kalanındaki kuralla aynı:
*AI bulgu üretmez, kural motoru üretir.* Şema üretimi bu kuralın dışında kalmış
tek yerdi.

**DDL gerçekten üretiliyor, "üretilebilir mi" diye tahmin edilmiyor.** Bu projede
birden çok kez görüldü: metin testleri geçen bir şema gerçek motorda reddedilebiliyor
(bkz. G45 — PostgreSQL hesaplanan kolonda tipi zorunlu kılıyor, SQLite collation'ı
tırnaksız kabul etmiyor).

**Yalnızca HATALAR düzeltme turuna giriyor, uyarılar değil.** Uyarıları döngüye
sokmak, modeli stil tercihleri için tur harcamaya iter.

**Başka motorun kısıtı bulgu DEĞİL, not.** Kullanıcı PostgreSQL istediyse
Oracle'ın diziyi desteklememesi onun sorunu değil; bunun için tur harcamak,
istenmemiş bir uyum uğruna bütçe yakmak olurdu. Yine de raporlanıyor
(`PortabilityNotes`) ki "bu şemayı yarın MySQL'e taşıyabilir miyim" sorusu
cevapsız kalmasın.

**Döngü sınırlı ve iyileşme yoksa erken duruyor.** Model aynı bulgularla
dönüyorsa bir tur daha aynı sonucu verir. Sınırsız bir döngü, modelin çözemediği
bir bulguda kullanıcının bütçesini sessizce tüketirdi.

**Sonuç GİZLENMİYOR.** Tur sınırına gelindiğinde şema yine dönüyor ama
`Clean = false` ve kalan bulgular listeleniyor. "Çalışıyor gibi görünen" bir şema
vermek, hiç vermemekten kötüdür: kullanıcı onu kullanmaya kalkar ve hata
veritabanında patlar.

**Kaç tur harcanacağını BÜTÇE söylüyor, hat değil.** Günlük hakkı bitmiş bir
kullanıcı için üç tur çalıştırmak, ona hiçbir şey vermeden parasını harcamak
olurdu. Bütçe bir tura bile yetmiyorsa hat hiç başlamıyor ve **429** dönüyor —
bu bir arıza değil, bir sınır.

**Groq dışındaki sağlayıcılarda eski tek-çağrı yolu korunuyor.** Ajan hattı Groq'a
bağlı; olmayan bir yolu varmış gibi davranmak Ollama/yerel motoru sessizce bozardı.

### Ölçüm

Harcanan **tur sayısı kadar** token düşülüyor. Bir tur da üç tur da aynı maliyete
sayılsaydı düzeltme döngüsü bedava görünür ve bütçe anlamını yitirirdi.

---

## Kapsam dışı

- **Belirsiz prompt'ta kullanıcıya soru sorma.** "Bir mağaza şeması yap" cümlesi
  çok şey söylemiyor; ideal ajan eksik bilgiyi sorardı. Bunun için hattın
  kullanıcıyla konuşabilmesi (çok adımlı oturum) gerekiyor ve bugünkü uç tek
  isteklik. Uydurmak yerine eksik bırakıldı.
- **Üretilen DDL'i gerçek bir veritabanında ÇALIŞTIRMAK.** Şu an yalnızca
  "derlenebiliyor mu" kontrol ediliyor. Gerçek çalıştırma altyapısı var
  (`BranchTestRunnerService`) ama her şema üretiminde bir container açmak, ücretsiz
  planda sunucuyu düşürür.
- **Prompt'un kendisinin iyileştirilmesi.** Hat, modele daha iyi bir istem yazmayı
  değil, çıktısını denetlemeyi çözüyor. İkisi ayrı işler.
