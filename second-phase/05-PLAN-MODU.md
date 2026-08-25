# 05 — Plan Modu

> **Sıra: 2.** Netleştirme ajanının doğal üst hali. İlk temasta özgünlüğü
> hissettiren yer.

---

## Ne

Hiç şema üretmeden, **konuşarak** işi netleştiren bir mod. Kullanıcı prompt'unu
yazıyor; sistem soruyor, kullanıcı cevaplıyor, sistem eksik kalan yerleri fark
edip tekrar soruyor. Sonunda ekranda **yazılı bir plan** oluyor:

> *"7 tablo kuracağım: users, products, variants, orders, order_items,
> payments, shipments. Varyantlı ürün seçtiğin için stok variant seviyesinde
> tutulacak. Ödeme ayrı tabloda, çünkü bir siparişin birden çok ödemesi olabilir.
> Onaylıyor musun?"*

Kullanıcı onaylayınca üretim başlıyor.

## Neden değerli

Bugün netleştirme **tek turluk**: 5 soru sorulup geçiliyor. Plan modu bunu
diyaloğa çeviriyor — Claude Code'un yaptığı şey.

Asıl kazanç şu: kullanıcı **üretimden önce** ne alacağını görüyor. Yanlışsa
konuşarak düzeltiyor, 200.000 token harcayıp canvas'ta görüp baştan başlamıyor.
Hem kalite artıyor hem maliyet düşüyor.

## Nasıl

- Var olan `ClarifyingQuestions` + `ArchetypeDetector` temel
- Üstüne **eksik tespiti**: cevaplardan sonra hâlâ belirsiz kalan noktalar
  (ör. "çok oyunculu" dedi ama eşleştirme mi lonca mı belli değil)
- Plan metni **deterministik üretilebilir** — tablo listesi ve gerekçeler
  cevaplardan çıkıyor. AI yalnızca metni akıcılaştırmak için gerekli
- Onay → mevcut üretim hattı, plan prompt'a ekleniyor

## ⚠️ Dikkat

- **Turlar sınırlı olmalı.** Sonsuz soru-cevap, kullanıcıyı yorup terk ettirir.
  3 tur yeter; sonrasında "elimdekiyle devam edeyim mi?" diye sor.
- **Her turda çıkış olmalı** — "soruları geç, şimdi üret" butonu her ekranda.
  Zorunlu hâle gelirse hızlı deneme yapmak isteyen kullanıcıyı kaybederiz.
- Plan **kaydedilebilir** olmalı: kullanıcı planı görüp yarın dönebilmeli.
- Soru üretimi mümkün olduğunca deterministik kalmalı; her turda AI çağırmak
  planın maliyetini üretimin maliyetine yaklaştırır ve amacı ters çevirir.

## 🔴 Yapılmayacak

- Serbest sohbet arayüzü. Bu bir **chatbot değil**; hedefi olan, biten,
  sonucu plan olan bir akış. Açık uçlu sohbet kutusu koyarsak kullanıcı
  "merhaba nasılsın" yazar ve ürün amacını kaybeder.
- Planı AI'ya "serbestçe" yazdırmak. Tablo listesi verilerden gelmeli;
  modelin uydurduğu bir plan, üretilecek şemadan farklı çıkabilir ve bu,
  onayı anlamsız kılar.
