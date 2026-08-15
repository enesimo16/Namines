# 01 — Pazar, Rakipler, Konumlandırma

> ## ⚠️ 2026-08 GÜNCELLEMESİ — bu dokümanın orijinal tezi kısmen geçersiz
>
> Bu doküman 2026-01'de yazıldı. 2026-08'de yapılan gerçek arama sonuçları, "Supabase'de
> görsel şema tasarımı yok" ve "kimse AI ile otomatik admin panel üretmiyor" iddialarının
> **artık doğru olmadığını** gösteriyor:
>
> - **Supabase görsel şema tasarımını gönderdi** ("Visual schema design lowers friction
>   for teams that think in diagrams before SQL"), + declarative schema diff, Index
>   Advisor, MCP server. [Kaynak: supabase.com/changelog](https://supabase.com/changelog/45702-developer-update-may-2026)
> - **Admin Pilot** — "connects via URI, uses AI to analyze your schema, predicts
>   workflows, groups tables by domain" — bu dokümandaki Console fikrinin neredeyse
>   birebir aynısı, zaten çalışan bir ürün. `supadmin.app`, Refine.dev, Softr gibi
>   benzerleri de var, çoğu ücretsiz.
> - **ChartDB** 22.000+ GitHub yıldızına ulaşmış, hâlâ fonsuz, açık kaynak — tasarım
>   katmanının en güçlü ücretsiz alternatifi olmaya devam ediyor.
> - **Xano** "Xano Agent" (agentic CLI, "tüm backend'i inşa edebiliyor") çıkardı.
>   **Retool** metinden tam uygulama üreten "AI AppGen" ekledi.
> - **Lovable/Bolt.new/v0/Replit Agent** — "tek prompt'tan UI+backend+DB+auth+deploy"
>   kategorisi artık milyar dolarlık değerlemelerle olgunlaşmış durumda.
>
> **Sonuç:** "Kategori tanımlayan, tüm platformu birleştiren geniş vizyon" penceresi
> kapanıyor — bu Supabase ve ekosisteminin, tek geliştiricinin sahip olamayacağı
> kaynak ve dağıtım gücüyle dolduracağı bir alan. **Ama** çoklu-motor (MSSQL/Oracle/
> MariaDB — hiçbiri Postgres-öncelikli değil), Türkiye/KVKK, ve en önemlisi
> **migration/schema evolution güvenliği** (üretim değil, değişim yönetimi) hâlâ
> boş. Bu iki gerekçeyle strateji [27-LIFECYCLE-PIVOT.md](27-LIFECYCLE-PIVOT.md)'te
> "geniş platform" → "lifecycle/governance" olarak daraltıldı. Aşağıdaki analiz
> **tarihsel bağlam** olarak kalıyor; güncel konumlandırma için önce 27'yi oku.

## 1. Pazar büyüklüğü (tahmini — kaynaklandırılmamış, gözlemsel)

> **Varsayım uyarısı:** Aşağıdaki rakamlar kamuya açık fiyat listeleri ve genel geliştirici nüfusu tahminlerinden türetilmiş kaba büyüklüklerdir; birincil pazar araştırması değildir.

| Segment | Yıllık pazar (kaba) | Namines'in payı |
|---|---|---|
| Veri modelleme / ERD araçları | ~$150M | Giriş kapısı (düşük gelir, yüksek trafik) |
| Backend-as-a-Service (Supabase/Firebase/Xano) | ~$3.5B | **Asıl hedef** |
| Internal tools (Retool/Appsmith/Budibase) | ~$2B | **Asıl hedef** |
| DB DevOps (Liquibase/Flyway/Bytebase/Atlas) | ~$800M | İkincil, kurumsal genişleme |
| Headless CMS / data admin (Directus/Strapi) | ~$1B | Örtüşen |

**Namines'in TAM'i tek başına ERD pazarı değil; SDBP kategorisi olarak ~$5-6B'lık kesişimdir.**

---

## 2. Rakip matrisi — detaylı

### 2.1 Tasarım tarafı (Faz 1'in rakipleri)

| Ürün | Fiyat | Güçlü | Zayıf | Namines'in cevabı |
|---|---|---|---|---|
| **dbdiagram.io** | Free / $9 / $14 kişi-ay | DBML standardı, çok yüksek mindshare, hızlı UX, embed | Canlı DB yok, API yok, AI yüzeysel, kod üretimi yok | DBML import/export ile kullanıcı çek, sonra Data Plane'e taşı |
| **ChartDB** | Free (MIT), Cloud ücretli | Açık kaynak, AI'lı, hızlı büyüyor, "connect & visualize" | Sadece görselleştirme, ürün derinliği yok, monetizasyon belirsiz | Aynı açıklıkta ol ama Data+App plane ile aş |
| **Azimutt** | Free / ~$15 | 1000+ tablolu legacy DB'lerde keşif, gerçekten iyi | UX ağır, dar niş, yazma yok | Introspection'ı ondan öğren, üstüne yazma+API koy |
| **DrawSQL** | Free / $14 | Güzel görsel, takım paylaşımı | Yüzeysel, motor desteği dar | UX barını yükselt |
| **Vertabelo / DbSchema / SqlDBM** | $25-100+ kişi-ay | Kurumsal, çok motorlu, olgun | Eski hissiyat, ağır, AI yok, pahalı | Fiyat + hız + AI ile alttan gir |
| **Luna Modeler / Navicat Modeler** | Tek seferlik lisans | Masaüstü, offline | Web değil, işbirliği yok | Web-first, collab |

### 2.2 Veri tarafı (Faz 2'nin rakipleri)

| Ürün | Fiyat | Güçlü | Zayıf | Namines'in cevabı |
|---|---|---|---|---|
| **Supabase** | Free / $25 / $599 | Postgres + Auth + Storage + Realtime + otomatik REST/GraphQL, dev sevgisi, açık kaynak | **Görsel modelleme yok**, AI-first tasarım yok, multi-engine yok (sadece PG), admin UI çok basit | Tasarım katmanı + gerçek admin Console + multi-engine |
| **Neon / PlanetScale** | Free / $19+ | DB branching'in en iyisi, serverless PG/MySQL | Sadece DB, uygulama katmanı yok | Onları **altyapı olarak kullan** (rakip değil, tedarikçi) |
| **Xano** | $59+ | No-code backend, ölçekli | Kapalı, pahalı, geliştirici sevmiyor, şema-görsel zayıf | Geliştirici-dostu + eject |
| **Firebase** | Kullanım bazlı | Ekosistem, gerçek zamanlı | NoSQL, ilişkisel değil, lock-in | İlişkisel + eject |
| **Hasura** | Free / $1.50/M req | GraphQL üstünlüğü, RLS | Var olan DB gerekir, UI yok, öğrenme eğrisi | Sıfırdan üretim + UI |
| **PocketBase** | Free (self-host) | Tek binary, çok hafif | Ölçeklenmez, SQLite, takım yok | Bulut + ölçek |

### 2.3 Uygulama tarafı (Console'un rakipleri)

| Ürün | Fiyat | Güçlü | Zayıf | Namines'in cevabı |
|---|---|---|---|---|
| **Retool** | $10-50 kişi-ay | Kurumsal standart, çok connector | **Var olan DB gerekir**, manuel UI kurma, pahalı, lock-in | Şemadan **sıfır konfigürasyonla** üret |
| **Directus** | Free / $15+ | Var olan DB'yi sarar, iyi admin UI, açık kaynak | Tasarım yok, AI yok, migration zayıf | Tasarım + AI + migration |
| **NocoDB / Baserow** | Free / $10+ | Airtable alternatifi, açık kaynak | Şema-first değil, geliştirici odaklı değil | Geliştirici odaklı |
| **Appsmith / Budibase** | Free / $20+ | Esnek UI builder | Manuel, DB tasarımı yok | Otomatik |
| **Forest Admin** | $0 / $16+ | Otomatik admin panel, iyi fikir | Var olan DB gerekir, kurulum ağır | Aynı fikir + DB'yi de biz üretiyoruz |

### 2.4 Görünmez rakip (en tehlikelisi)

| "Ürün" | Neden tehlikeli | Namines'in cevabı |
|---|---|---|
| **Claude Code / Cursor / Copilot** | 10 saniyede Prisma şeması + migration + admin sayfası üretir, bedava, IDE'den çıkmadan | Kod üretmek değil, **çalışan altyapı sağlamak**. LLM sana DB provision edemez, RLS uygulayamaz, ekibine panel açamaz, migration'ı üretimde güvenle uygulayamaz. |
| **Sadece elle yazmak** | Deneyimli geliştiricinin varsayılanı | Eject ile "elle yazmaya" istediğin an dönebilirsin — bu itirazı öldürür |

---

## 3. Pazar boşluğu — tek cümlede

> **Supabase'in veri katmanı + Retool'un uygulama katmanı + dbdiagram'ın tasarım katmanı, tek bir doğruluk kaynağından türetilmiş halde hiçbir üründe yok.**

Bugün bir geliştirici bunu şöyle yapıyor:
```
dbdiagram'da çiz → elle SQL'e çevir → Supabase'e yapıştır →
Prisma şemasını elle yaz → Retool'a bağla → her tablo için elle UI kur →
şema değişince 4 yerde birden elle güncelle  ← BURASI ACI
```
Namines'te:
```
Studio'da tasarla → Deploy → bitti. Şema değişince her şey senkron.
```

**Kanca cümlesi (landing page H1 adayı):**
> *"Change the schema. Everything else updates itself."*

---

## 4. Konumlandırma ifadesi (positioning statement)

> **Hızlı hareket eden ürün ekipleri ve solo geliştiriciler için**,
> ki bunlar veri modeli değiştikçe DB, API ve admin panelini elle senkron tutmaktan yoruluyor,
> **Namines** bir *schema-driven backend platformudur*
> ki tasarımdan canlı veritabanına, otomatik API'ye ve yönetim paneline kadar her şeyi tek kaynaktan üretir ve senkron tutar.
> **Supabase veya Retool'un aksine**, Namines şemayı birinci sınıf, sürümlenebilir, görsel bir artefakt olarak ele alır — ve istediğin an kod olarak eject etmene izin verir.

---

## 5. Fiyat kıyaslaması

| Ürün | Giriş | Pro | Takım |
|---|---|---|---|
| dbdiagram | $0 | $9 | $14/kişi |
| Supabase | $0 | $25/proje | $599 |
| Retool | $0 | $10/kişi | $50/kişi |
| Directus Cloud | $0 | $15 | $99 |
| Forest Admin | $0 | $16/kişi | özel |
| **Namines (öneri)** | **$0** | **$19/ay** | **$39/kişi-ay** | 

Detay: [22-BUSINESS-MODEL.md](22-BUSINESS-MODEL.md)

---

## 6. Savunma hendeği (moat) analizi

| Hendek adayı | Güç | Not |
|---|---|---|
| Özellik sayısı | ❌ zayıf | Kopyalanır |
| AI şema üretimi | ❌ zayıf | Emtia |
| **NSL formatı + ekosistem** | ⚠️ orta | DBML gibi standart olursa güçlü. Açık kaynak yap, spesifikasyonu yayınla. |
| **Üç plane senkronizasyonu** | ✅ güçlü | Mühendislik olarak zor, kopyalaması pahalı |
| **Kullanıcının verisi platformda** | ✅✅ en güçlü | Değiştirme maliyeti yüksek |
| **Golden-file doğruluk sertifikası** | ✅ güçlü | "6 motorda %100 çalışan DDL" kanıtlanabilir bir iddia, rakiplerin çoğunda yok |
| .NET/MSSQL derinliği | ✅ güçlü (niş) | Rakiplerin %90'ı JS/PG-first. Kurumsal .NET dünyası ihmal edilmiş. |
| Marka / topluluk | ⚠️ zamanla | GTM'e bağlı |

**Sonuç:** Hendek, "veri platformda + üç katman senkron + kanıtlanmış doğruluk" üçlüsünde. Bunlara yatırım yap; özellik yarışına girme.
