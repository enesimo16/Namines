# Namines Strategic Product & Risk Assessment Report

Bu stratejik değerlendirme raporu; **Namines** veritabanı tasarım ve yönetim platformunun sektörde rakiplerine karşı ezici bir üstünlük kurmasını sağlayacak yenilikçi ürün fikirlerini, mevcut teknik riskleri, sorunları ve bunları bertaraf edecek mimari çözüm planlarını içermektedir.

---

## 🚀 1. Sektörde Öncü Olmayı Sağlayacak Ürün Fikirleri (Market Leadership Roadmap)

Namines'i basit bir diyagram çizim aracından çıkarıp, dünya standartlarında otonom bir **AI-Driven Database Platform (AIDBP)** haline getirecek yenilikçi yol haritası:

### A. Görsel Git-Like Şema Versiyon Kontrolü (Visual Schema Branching & Versioning)
* **Konsept:** Yazılımdaki Git mantığını tamamen görsel veritabanı şemalarına taşımak.
* **Detay:** Kullanıcılar veritabanı tasarımlarında görsel dallanmalar (Branch: `main`, `feature/billing-tables`) yapabilir, şemalarını görsel commit'lerle kaydedebilir. 
* **Fark Yaratan Özellik:** İki farklı versiyonu yan yana görsel olarak karşılaştırıp (Visual Diff), çakışmaları (Conflicts) sürükle-bırak yöntemiyle çözen otonom bir merge mekanizması.

### B. Tarayıcı İçi Canlı Veri Gezgini ve SQL Konsolu (Embedded Live SQL Explorer)
* **Konsept:** Sandbox container'ı ayağa kalktığında kullanıcının sadece dosyayı indirmesini beklemek yerine, tarayıcı içinde canlı veritabanıyla etkileşime geçebilmesi.
* **Detay:** Docker konteyneri ayağa kalktığı an, tarayıcıda glassmorphism tasarımlı otonom bir **SQL Editor ve Data Explorer** açılır. 
* **Fark Yaratan Özellik:** Kullanıcılar şemaya göre otomatik üretilen 50 mock satırı anlık görebilir, sunucuya hiç bağlanmadan tarayıcı içinden canlı SQL sorguları çalıştırıp diyagramlarındaki tabloları test edebilir.

### C. Otonom Zero-Code Staging Canlı Geçiş (One-Click Migration Applicator)
* **Konsept:** Üretilen SQL geçiş kodlarını sadece kopyalatmak yerine staging/test sunucusuna tek tıkla uygulamak.
* **Detay:** Kullanıcılar kendi staging/dev veritabanı bağlantı bilgilerini güvenli bir şekilde (SSO/Vault) girerek, canvas üzerinde yaptıkları görsel değişiklikleri tek tıkla canlı veritabanlarına yansıtırlar.
* **Fark Yaratan Özellik:** Sistem, şemadaki yıkıcı değişikliklerin (Destructive changes) veritabanına uygulanmasından önce canlı şemayı analiz eder ve otomatik olarak veriyi koruyan otonom ara-geçiş tabloları kurarak sıfır veri kaybı garantisi verir.

### D. Bulut Performans ve Maliyet Optimizasyon Danışmanı (AI FinOps & Performance Advisor)
* **Konsept:** Şemayı sadece tasarlamakla kalmayıp, bulut sağlayıcılardaki (AWS RDS, Azure SQL, GCP Cloud SQL) maliyet ve sorgu performanslarını önceden tahmin etmek.
* **Detay:** AI DBA motoru, oluşturulan tabloların boyutlarını ve tahmini sorgu yüklerini analiz eder.
* **Fark Yaratan Özellik:** *"Bu şemayı AWS üzerinde çalıştırırsanız aylık $240 tutar. Ancak şu NVARCHAR alanlarını sınırlar ve Foreign Key kolonlarına Index eklerseniz, CPU yükünüz %40 azalır ve $120'lık pakete düşebilirsiniz"* gibi otonom kararlar ve somut optimizasyon kodları sunması.

### E. Otonom Tam-Katman CRUD API Üreticisi (Visual Full-Stack API Scaffolder)
* **Konsept:** Veritabanından sadece model kodu değil, çalışan bir mikroservis projesi üretmek.
* **Detay:** Canvas şeması onaylandığı an, seçilen dilde (ASP.NET Core REST API, NestJS, Fast API) tamamen hazır; Repository deseni, Validation kuralları, Swagger dokümantasyonu ve Dockerfile dosyası eklenmiş tam teşekküllü bir API projesini `.zip` paketi olarak indirme fırsatı.

---

## ⚠️ 2. Teknik Riskler ve Sorun Analizi (Risk Registry & Technical Debt)

Projenin kararlı bir SaaS ürününe dönüşürken karşılaşacağı en büyük teknik engeller ve çözüm stratejileri:

### Risk 1: LLM JSON Çıktılarındaki Kaçış Karakteri Hataları (JSON Escape Character Vulnerability)
* **Açıklama:** Groq API üzerinden Llama modellerinden C# kodları veya JSON yapıları istenirken, modeller kod satırlarındaki yeni satır (`0x0A` veya `\n`) karakterlerini JSON dizesi içinde doğru şekilde kaçış (escape) karakterine dönüştürmeden ham olarak döndürebiliyor. Bu durum C# tarafında `System.Text.Json.JsonException: '0x0A' is invalid within a JSON string` hatası fırlatarak isteklerin 500 hatasıyla kırılmasına yol açıyor.
* **Çözüm Stratejisi:**
  1. `GroqAIService.cs` içerisindeki JSON deserialize işlemleri öncesinde, dönen ham string metni regex filtrelerinden geçirerek tırnak içindeki kontrolsüz yeni satır (`\n`) ve tab (`\t`) karakterlerini otomatik kaçışlı (`\\n`, `\\t`) yapılara dönüştüren otonom bir **JSON Sanitize Preprocessor** katmanı yazılmalıdır.
  2. Modellerin sistem promptlarında JSON Mode (`response_format: { type: "json_object" }`) parametresi aktif olarak zorlanmalı ve her prompta kaçış kuralları birer test case olarak enjekte edilmelidir.

### Risk 2: Çoklu Kullanıcı Ortamlarında Docker Kaynak Sızıntıları (Concurreny & Resource Leakage)
* **Açıklama:** MSSQL resmi imajı minimum 1GB RAM ve ciddi bir CPU payı talep etmektedir. Birden fazla kullanıcının eşzamanlı olarak Sandbox başlattığı durumlarda (Concurreny), sunucunun RAM kapasitesi hızla tükenecek ve konteynerler başlatılamayarak `Exception: Veritabanı başlatılamadı veya sağlık kontrolü zaman aşımına uğradı` hatası fırlatacaktır. Ayrıca işlem sırasında çöken veya tarayıcıyı kapatan kullanıcıların arkasında durdurulmamış yetim (zombie) konteynerler kalma riski çok yüksektir.
* **Çözüm Stratejisi:**
  1. Sunucu üzerinde çalışan yetim konteynerleri otonom olarak avlayan ve 5 dakikadan uzun süredir yanıt vermeyen sandboxları imha eden arka plan bir **Docker Sweeper Service (Cron)** kurulmalıdır.
  2. Gerçek Docker konteynerleri yerine, eşzamanlı hafif yükler için tarayıcıda WebAssembly üzerinde koşan hafif SQL motorları veya sunucu tarafında milisaniyeler içinde ayağa kalkan **SQLite/Postgres Pool** havuzları öncelikli test katmanı yapılmalı; Docker Sandbox ise sadece nihai `.bak` üretimi için kuyruğa (Queue) alınarak çalıştırılmalıdır.

### Risk 3: Yıkıcı Şema Değişikliklerinin Yanlış Yorumlanması (Destructive Migration Flaws)
* **Açıklama:** Mevcut şema diferansiyel motoru (Diff), tablo ve sütunları isim bazlı eşleştirir. Eğer kullanıcı canvas üzerinde `Users` tablosunun ismini `AppUsers` olarak değiştirirse; diff motoru bunu bir **Tablo Silme (Users)** ve **Yeni Tablo Ekleme (AppUsers)** olarak algılar. Bu geçiş canlıya uygulandığında **tüm kullanıcı verileri tamamen silinir!**
* **Çözüm Stratejisi:**
  1. Tablolara ve sütunlara isimlerinden bağımsız, canvas üzerinde kalıcı ve değişmeyen dahili kimlikler (stable metadata UUIDs) atanmalıdır.
  2. Şema karşılaştırmasında isim değişse bile dahili UUID'si eşleşen yapılar tespit edilerek, üretilen migration kodunun `RenameTable` veya `RenameColumn` olarak otonom derlenmesi güvence altına alınmalıdır.

### Risk 4: Karmaşık El Çizimlerinde Vision API Doğruluk Toleransı (Vision Interpretation Tolerances)
* **Açıklama:** Beyaz tahtaya çizilen okların (Foreign Key) tam olarak hangi kolondan hangi kolona gittiği el yazısının karmaşıklığından ötürü Vision modeli tarafından yanlış yorumlanabilir. Bu durum, hatalı veritabanı kurgularına sebebiyet verir.
* **Çözüm Stratejisi:**
  * Görselden import bittikten hemen sonra kullanıcıya, AI'ın algıladığı ilişkileri listeleyen interaktif bir **"İlişki Doğrulama ve İlişkilendirme Sihirbazı"** (Import Verification Wizard) sunulmalı; kullanıcı onayından sonra canvasa aktarım yapılmalıdır.

---

## 🧭 3. Yönetici Özeti ve Stratejik Aksiyon Planı

| Öncelik | Aksiyon Başlığı | Etkisi | Çaba Seviyesi | Risk Azaltma Hedefi |
| :--- | :--- | :--- | :--- | :--- |
| **Kritik** | **JSON Preprocessor Sanitize Filtresi** | Çok Yüksek | Düşük | API deserialization hatalarını %100 engellemek. |
| **Yüksek** | **Docker Sweeper Background Service** | Yüksek | Orta | Sunucu bellek sızıntılarını ve zombie container kilitlenmelerini çözmek. |
| **Yüksek** | **UUID Tabanlı Tablo Takibi** | Çok Yüksek | Orta | Canlı geçişlerdeki yıkıcı veri kaybı riskini tamamen yok etmek. |
| **Orta** | **SQL Canlı Explorer Konsolu** | Sektörde Liderlik | Yüksek | Kullanıcı deneyimini rakiplerin fersah fersah ötesine taşımak. |
