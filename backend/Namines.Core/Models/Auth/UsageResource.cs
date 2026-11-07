namespace Namines.Core.Models.Auth;

/// <summary>
/// Ölçülen kaynak türleri (new-phase/22-BUSINESS-MODEL.md §5).
///
/// Enum, serbest metin değil: kaynak adı faturaya giriyor ve her çağıranın kendi
/// yazımını kullanması ("ai_call", "aiCall", "AI") aynı kullanımı farklı kalemler
/// olarak sayıp faturayı sessizce yanlış çıkarırdı. Sözleşme tipi olduğu için Core'da.
/// </summary>
public enum UsageResource
{
    AiCall = 0,
    ApiRequest = 1,
    BranchDatabase = 2,
    StorageGigabyteMonth = 3,
    ConsoleUser = 4,
    DataTransferGigabyte = 5,
}
