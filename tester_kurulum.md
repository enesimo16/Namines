# 🚀 Namines — Tester Kurulum Kılavuzu

## Ön Koşullar (Bir kez kur)

| Araç | İndirme |
|------|---------|
| Docker Desktop | https://www.docker.com/products/docker-desktop |
| Git | https://git-scm.com/downloads |

> Docker Desktop'ı kurduktan sonra bilgisayarı yeniden başlatın.

---

## 1. Projeyi İndir

```bash
git clone https://github.com/KULLANICI/namines.git
cd namines
```

> Repo henüz public değilse zip olarak al: **db klasörünü** masaüstüne çıkar.

---

## 2. API Key Dosyasını Oluştur

Proje kök dizininde (`.env` dosyası) şunu oluştur:

```bash
# Windows PowerShell:
@"
GROQ_API_KEY=gsk_SENDEN_ALINACAK_KEY
JWT_KEY=NaminesTestKey_Min32Chars_Test2026!
"@ | Out-File -FilePath .env -Encoding utf8
```

> **GROQ_API_KEY** → Enes'ten alacaksın.  
> **JWT_KEY** → Yukarıdaki değer test için yeterli.

---

## 3. Başlat (Tek Komut!)

```bash
docker compose up --build
```

İlk seferinde **3-5 dakika** sürer (imajlar build edilir). Sonraki açılışlarda **30 saniye**.

✅ Hazır olunca terminalde şunu görürsün:
```
namines-frontend    | ✓ Ready in Xs
namines-backend     | Now listening on http://[::]:8080
```

---

## 4. Uygulamayı Aç

| Servis | URL |
|--------|-----|
| 🌐 Uygulama (Frontend) | http://localhost:3000 |
| 🔧 Backend API | http://localhost:5000 |
| 📚 Swagger (API Dökümantasyonu) | http://localhost:5000/swagger |

---

## 5. Test Senaryoları

### ✅ Temel Akış
1. http://localhost:3000 aç
2. Textarea'ya bir şema gir: *"Bir e-ticaret veritabanı tasarla: kullanıcılar, ürünler, siparişler"*
3. **"Mimarimi Üret"** butonuna bas
4. Canvas sayfasında tablolar ve ilişkiler çıkmalı
5. Sağ tık → tablo ekle/düzenle

### ✅ SQL Derleme
1. Canvas'ta **"Derle"** butonuna bas
2. DDL Script, EF Core, Mermaid ER diyagramları görünmeli
3. **SQL Server / PostgreSQL / MySQL** arasında geçiş yapılabilmeli

### ✅ Test Verisi Üretimi
1. "Test Verileri" sekmesine geç
2. Sektör seç (veya Auto Detect bırak)
3. **"Generate Test Data"** butonuna bas
4. SQL seed script çıkmalı

### ✅ Kayıt / Giriş
1. Header'da **"Giriş Yap / Kayıt Ol"** butonuna bas
2. Bireysel veya Kurumsal hesap oluştur
3. Projeler cloud'a kaydedilmeli

### ✅ DBA Analizi
1. Canvas'ta bir şema oluşturduktan sonra toolbar'daki kalkan ikonuna bas
2. DBA paneli açılmalı (skor, uyarılar, öneriler)
3. **"Yapay Zeka ile Tüm Hataları Düzelt"** butonunu test et

---

## 6. Durdurma

```bash
# Durdur (veriler korunur)
docker compose down

# Durdur + tüm verileri sil (temiz başlangıç)
docker compose down -v
```

---

## Sık Karşılaşılan Sorunlar

| Sorun | Çözüm |
|-------|-------|
| Port 3000 meşgul | `docker compose down` çalıştır, sonra tekrar `up` |
| "Cannot connect to Docker" | Docker Desktop'ı aç ve bekle |
| API hataları (500) | `.env` dosyasında `GROQ_API_KEY` doğru mu kontrol et |
| Yavaş ilk açılış | Normaldir, imajlar build ediliyor |

---

## Log Kontrolü

```bash
# Tüm loglar
docker compose logs -f

# Sadece backend
docker compose logs -f namines-backend

# Sadece frontend
docker compose logs -f namines-frontend
```
