# 08 — Prompt Deneyimi: Sorular, Geçmiş, Kapsam

> **Sıra: 5.** Ucuz cila. Her biri küçük, birlikte hissedilir fark yaratıyor.

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
