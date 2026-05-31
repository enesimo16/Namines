using System.Text.Json;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Prompts;

public static class SmartSeedPromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return @"Sen Türkiye'de yaşayan kıdemli bir veri mühendisi ve SQL veri tabanı uzmanısın.
Görevin, sana verilen şema için domain-spesifik, gerçekçi ve tamamen Türkiye şartlarına uygun test verisi (Seed INSERT SQL script'i) üretmektir.

TEKNİK VE İÇERİKSEL KATILAR:
1. TÜRKİYE SPESİFİK VERİ: İsimler gerçek Türk isimleri (Örn: Ahmet Yılmaz, Merve Demir), şehirler gerçek Türkiye şehirleri (Örn: İstanbul, Bursa, Sakarya), telefonlar +90 5xx formatında, TC kimlikler 11 haneli geçerli formatta, plakalar (Örn: 34 ABC 123) ve fiyatlar TL bazında gerçekçi olmalıdır.
2. FK İLİŞKİ TUTARLILIĞI: İlişkili tablolarda Foreign Key (FK) alanları için ürettiğin parent ID'lerini AYNEN kullan. child satırlardaki referans bütünlüğünü (referential integrity) KESİNLİKLE bozma. Parent tablolardaki ID değerlerini (1'den rowCount'a kadar) referans olarak child tablolardaki FK kolonlarında kullan.
3. TABLO SIRALAMASI: SQL script'inde INSERT'leri FK ilişkilerine göre doğru sırada yaz. Önce bağımsız (parent) tablolar, ardından onlara referans veren bağımlı (child) tablolar insert edilmelidir.
4. HEDEF VERİ TABANI SYNTAX'I: SQL syntax'ı verilen hedef veritabanı tipine (MSSQL, PostgreSQL, MySQL vb.) tamamen uygun olmalı, uygun tırnak ve veri formatları kullanılmalıdır.
5. TEMİZ ÇIKTI: Sadece SQL INSERT statement'larını döndür. Markdown kod blokları (```sql), açıklamalar, yorumlar veya açıklayıcı metinler KESİNLİKLE ekleme.";
    }

    public static string BuildUserPrompt(DatabaseSchema schema, DatabaseType dbType, string domain, int rowCount)
    {
        var schemaJson = JsonSerializer.Serialize(schema);
        return $@"Hedef Veritabanı Tipi: {dbType}
Algılanan Sektör/Domain: {domain}
Şema Detayları (JSON):
{schemaJson}

Her tablo için yaklaşık {rowCount} adet gerçekçi, FK-tutarlı ve Türkiye verilerine uygun test INSERT statement'ları üret.";
    }
}
