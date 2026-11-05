namespace Namines.BuildingBlocks.Application;

public enum ModuleOutcomeKind
{
    Ok,
    NotFound,
    Invalid,
    Conflict,
    Forbidden,
    TooMany,
    Failed,
}

/// <summary>
/// Aktörlü modül işlemlerinin sonucu. Beklenen iş kuralı retleri istisna değil: controller durum koduna
/// çevirir. Yetkisiz ile yok çoğu yerde aynı sonuçtur (<see cref="ModuleOutcomeKind.NotFound"/>): başkasının
/// kaydının varlığı sızmasın.
/// </summary>
public sealed record ModuleOutcome<T>(ModuleOutcomeKind Kind, T? Value, string? Error, int? StatusCode = null, string? Code = null)
{
    public bool IsOk => Kind == ModuleOutcomeKind.Ok;

    public static ModuleOutcome<T> Ok(T value) => new(ModuleOutcomeKind.Ok, value, null);
    public static ModuleOutcome<T> NotFound(string? error = null) => new(ModuleOutcomeKind.NotFound, default, error);
    public static ModuleOutcome<T> Invalid(string error) => new(ModuleOutcomeKind.Invalid, default, error);
    public static ModuleOutcome<T> Conflict(string error) => new(ModuleOutcomeKind.Conflict, default, error);
    public static ModuleOutcome<T> Forbidden(string? error = null) => new(ModuleOutcomeKind.Forbidden, default, error);
    public static ModuleOutcome<T> TooMany(string error) => new(ModuleOutcomeKind.TooMany, default, error);

    /// <summary>Altyapı ya da başka modülün sonucu: durum kodu ve makine kodu olduğu gibi taşınır.</summary>
    public static ModuleOutcome<T> Failed(int statusCode, string? error, string? code) =>
        new(ModuleOutcomeKind.Failed, default, error, statusCode, code);
}
