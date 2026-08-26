# 08 — Prompt Deneyimi: Sorular, Geçmiş, Kapsam

> **Sıra: 5.** Ucuz cila. Her biri küçük, birlikte hissedilir fark yaratıyor.
>
> ✅ **Dördü de yapıldı.**
> - **8.1** — `PlanBuilder`'ın var olan "belirsizlik" mekanizması (bkz. 05)
>   kullanılarak iki yeni ikinci-seviye soru eklendi: Ecommerce'de "varyantların
>   kendi fiyatı/SKU'su olacak mı", Erp'de "stok şirketler arasında ortak mı,
>   ayrı mı". İkisi de gerçekten tablo şeklini değiştiriyor (product_variants'a
>   kolon eklenmesi / warehouses tablosu). Doc'un kendi örnekleri
>   ("pazaryeri mi tek satıcı mı", "çok şirketli mi") zaten farklı bir
>   archetype (`Marketplace`) ve çekirdek `companies` sorusu olarak
>   önceden çözülmüş durumdaydı — tekrar yazılmadı.
> - **8.2** — `ClarifyDialog`'a 500 karakter sınırlı "Anything else to add?"
>   kutusu eklendi. Sınır hem istemcide (`maxLength`) hem sunucuda (`SchemaController`,
>   savunma amaçlı) uygulanıyor. Cevap sayacına dahil edilmiyor — makine
>   okunur bir soru cevabı değil.
> - **8.3** — Canvas'taki `RegionalPromptPanel`'e istemci-taraflı, en fazla 15
>   kayıtlı prompt geçmişi eklendi (Zustand `persist`, yalnızca UI tercihi
>   olarak — sunucuda sohbet oturumu YOK). Tıklanınca kutuyu dolduruyor,
>   göndermiyor.
> - **8.4** — Kapsam etiketi eklendi: "N table(s) selected — only these will
>   change" / "No table selected — the whole schema may change". Panel zaten
>   var olan seçili-tablo çipi mantığını okuyor, yeni bir kapsam kavramı
>   icat etmiyor.
>
> Ayrıca bu işe başlarken bağımsız bir **çökme hatası** bulundu ve düzeltildi:
> SSE üretim akışı (`SchemaController.WriteEventAsync`) `JsonSerializer.Serialize`'ı
> uygulamanın camelCase ayarları OLMADAN çağırıyordu — `AgentStep.Kind`/`Message`
> tel üzerinde `"Kind"/"Message"` (büyük harfle) gidiyordu, ön yüz `step.kind`
> okuyunca `undefined` buluyor, `ProductionScreen` bunu bir ikon bileşenine
> geçirince tüm React ağacı çöküyordu. Kullanıcının bildirdiği "prompt sonrası
> canvas çalışmıyor" tam olarak buydu — üretim ekranı ilk adımda çöktüğü için
> kimse canvas'a hiç ulaşamıyordu. Artık paylaşılan `JsonSerializerOptions`
> kullanılıyor; curl ile tel formatı ve canlı UI akışıyla doğrulandı.

---

## 8.1 Daha fazla ve daha alan-özel soru

Bugün 14 iş türü, tür başına en fazla 5 soru. Genişletilecek yer:

- **Tür sayısı değil, tür başına derinlik.** 14 tür yeterli; e-ticarette
  "pazaryeri mi tek satıcı mı", ERP'de "çok şirketli mi" gibi **ikinci
  seviye** sorular daha çok kazandırır.
- Soru bankası deterministik kalmalı — bugünkü en büyük avantajı bu (sıfır token).

## 8.2 "Eklemek istedikleriniz" alanı

Soruların altına serbest metin kutusu. Kullanıcı "ayrıca iade süreci de olsun"
yazıyor, AI bunu yorumlayıp prompt'a yapılandırılmış biçimde ekliyor.

**Neden değerli:** sabit sorular her şeyi kapsayamaz. Bu kutu, kapsanmayanı
yakalıyor ve hangi soruların eksik olduğunu **zamanla bize öğretiyor** — en çok
ne yazıldığına bakıp o soruyu bankaya ekleriz.

## 8.3 Canvas'ta prompt geçmişi

Canvas'taki prompt kutusuna bir buton: daha önce ne istendiğini gösteren liste.
Kullanıcı geçmiş bir isteği tekrar çalıştırabilmeli ya da düzenleyip
gönderebilmeli.

## 8.4 Seçime göre kapsam

- **Bir tablo seçiliyken** → düzeltme o tabloya odaklanır
- **Hiçbir şey seçili değilken** → genel düzenleme

> **Not:** Bunun yarısı zaten var — `RegionalPromptPanel` seçili tablolarla
> çalışıyor. Eksik olan, kapsamın kullanıcıya **görünür** olması: kutunun
> üstünde "3 tablo seçili — yalnızca onlar değişecek" gibi net bir etiket.

## ⚠️ Dikkat

- **Kapsam görünür olmalı.** Kullanıcı neyin değişeceğini göndermeden önce
  bilmeli. "Sadece seçili tablolar" yazmadan bunu yapmak, beklenmedik
  değişikliklere ve güven kaybına yol açar.
- Geçmiş **kullanıcıya ait** — ekip planında başkasının prompt'unu göstermek
  gizlilik sorunu olur. Ekip geçmişi ayrı ve açık rızayla.
- Serbest metin kutusu prompt'a **ham** eklenmemeli; yorumlanıp
  yapılandırılmalı, yoksa 06'daki URL hatasının küçük bir kopyası olur
  (sınırsız metin → kota).

## 🔴 Yapılmayacak

- Prompt geçmişini sunucuda kalıcı **sohbet oturumu** hâline getirmek. Bu bir
  chatbot değil; geçmiş bir kolaylık, bir konuşma bağlamı değil.
- Soruları modele ürettirmek. Bugünkü avantaj (sıfır token, kararlı sorular)
  tam olarak buradan geliyor.
