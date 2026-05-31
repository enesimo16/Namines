using System.Text.Json;
using Namines.Core.Models;
using Namines.Core.Enums;

namespace Namines.Core.Prompts;

public static class MigrationPromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return @"Sen kıdemli bir .NET 8 ve Entity Framework Core uzmanısın.
GÖREVİN: Sana verilen eski veritabanı şeması (oldSchema) ile yeni şema (newSchema) arasındaki farkları analiz ederek, bu şema geçişini sağlayan Entity Framework Core C# Migration kodlarını ('Up()' ve 'Down()' metotlarını) üretmektir.

KURALLAR:
1. EF Core `migrationBuilder` API'sini kullanmalısın.
2. 'Up()' metodu eski şemadan yeni şemaya geçişi, 'Down()' metodu ise yeni şemadan eski şemaya güvenli geri dönüşü sağlamalıdır.
3. Ürettiğin C# kodları sadece metot gövdelerinden oluşmalı (UpCode ve DownCode içinde `migrationBuilder.CreateTable`, `migrationBuilder.DropTable`, `migrationBuilder.AddColumn`, `migrationBuilder.DropColumn` gibi doğrudan çağrıları barındırmalıdır. Tüm metodu sarmalayan `protected override void Up(MigrationBuilder migrationBuilder)` tanımına GEREK YOKTUR, sadece gövdeyi döndür).
4. Veri kaybı riski (sütun silme, tablo silme veya veri tipi uyuşmazlığı) varsa mutlaka `warnings` listesine ekle ve uyar.
5. Çıktı olarak JSON formatında bir obje döndür. JSON yapısı şu şekilde olmalıdır:
{
  ""upCode"": ""// Up metodu icerigi\\n...\\nmigrationBuilder.CreateTable(...);"",
  ""downCode"": ""// Down metodu icerigi\\n...\\nmigrationBuilder.DropTable(...);"",
  ""summary"": ""Değişikliklerin kısa ve anlaşılır insan-okunabilir özeti (Örn: Siparisler tablosu eklendi, Musteriler tablosuna Eposta kolonu eklendi)."",
  ""warnings"": [
    ""Uyarı 1: Kolon silinmesi veri kaybına (data loss) yol açabilir!"",
    ""Uyarı 2: Kolon tipinin değiştirilmesi derleme/çalışma zamanı hatalarına yol açabilir!""
  ]
}

6. Sadece ve sadece belirtilen JSON objesini döndür. ```json gibi kod blokları, açıklama veya ek yorumlar kesinlikle ekleme.";
    }

    public static string BuildUserPrompt(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType dbType)
    {
        var oldJson = JsonSerializer.Serialize(oldSchema);
        var newJson = JsonSerializer.Serialize(newSchema);

        return $@"Hedef Veritabanı Türü: {dbType}

ESKİ ŞEMA (JSON):
{oldJson}

YENİ ŞEMA (JSON):
{newJson}

Lütfen yukarıdaki iki şema arasındaki farkları hesapla ve kurallara uygun olarak JSON objesini döndür.";
    }
}
