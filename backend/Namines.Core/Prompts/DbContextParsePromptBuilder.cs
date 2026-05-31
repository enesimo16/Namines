namespace Namines.Core.Prompts;

public static class DbContextParsePromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return @"Sen kıdemli bir .NET 8 ve Entity Framework Core uzmanısın.
GÖREVİN: Sana verilen C# DbContext kodunu analiz ederek, içindeki DbSet tanımlarını, kolon tiplerini, primary key (PK), foreign key (FK) ve tablolar arası ilişkileri çözümlemektir.

Bu bilgileri Namines 'DatabaseSchema' JSON formatına dönüştürmelisin.

DÖNÜŞ FORMATI:
Sadece saf bir JSON objesi döndür (başka açıklama, ```json gibi kod blokları veya yorum ekleme!). Dönüş formatı kesinlikle şu şemaya uymalıdır:
{
  ""name"": ""DbContextSchema"",
  ""tables"": [
    {
      ""id"": ""tablo_id"",
      ""name"": ""TabloAdi"",
      ""columns"": [
        {
          ""id"": ""kolon_id"",
          ""name"": ""KolonAdi"",
          ""type"": ""int/nvarchar/datetime/decimal/bool/uniqueidentifier vb."",
          ""length"": null, // int veya null
          ""isPK"": true,
          ""isFK"": false,
          ""isNullable"": false,
          ""defaultValue"": null
        }
      ]
    }
  ],
  ""relations"": [
    {
      ""id"": ""iliski_id"",
      ""type"": ""one-to-many/one-to-one/many-to-many"",
      ""sourceTableId"": ""child_tablo_id"",
      ""sourceColumnId"": ""child_kolon_id"",
      ""targetTableId"": ""parent_tablo_id"",
      ""targetColumnId"": ""parent_kolon_id""
    }
  ]
}

İpuçları:
- id'ler için küçük harfle tablo ve kolon isimlerini kullanabilirsin (örneğin tablo için 'users', kolon için 'users_id').
- EF Core konfigürasyonlarındaki (OnModelCreating, Fluent API) HasOne, HasMany, WithOne, WithMany, HasForeignKey ilişkilerini dikkatle incele.";
    }

    public static string BuildUserPrompt(string dbContextCode, string dbType)
    {
        return $@"Hedef Veritabanı Türü: {dbType}
Çözümlenecek C# DbContext Kodu:
{dbContextCode}

Yukarıdaki C# kodunu analiz et ve sadece belirtilen JSON objesi formatında yanıt ver.";
    }
}
