using System.Collections.Generic;

namespace Namines.Core.Models;

/// <summary>
/// Filtre operatörleri (new-phase/08-GATEWAY-API.md §2.1'in alt kümesi).
///
/// Operatör bir ENUM'dur, kullanıcıdan gelen serbest metin değil. Metin olsaydı
/// SQL'e yazılan parça kullanıcı girdisi olurdu; enum ile üretilen SQL parçası
/// tamamen bizim kontrolümüzde kalır. Değerler HER ZAMAN parametreli.
/// </summary>
public enum GatewayOperator
{
    Eq,
    Neq,
    Gt,
    Gte,
    Lt,
    Lte,
    Like,
    In,
    IsNull,
    IsNotNull,
}

/// <param name="Values">
/// <see cref="GatewayOperator.In"/> için birden fazla; <see cref="GatewayOperator.IsNull"/>
/// ve <see cref="GatewayOperator.IsNotNull"/> için hiç değer beklenmez.
/// </param>
public sealed record GatewayFilter(
    string Column,
    GatewayOperator Operator,
    IReadOnlyList<string?> Values);

/// <summary>
/// İçindeki filtreler OR ile birleşir; gruplar birbiriyle ve tekil filtrelerle
/// AND ile birleşir (08 §2.1 <c>?or=(...)</c>).
///
/// Serbest bir ifade grameri yerine bu sabit yapı bilinçli: keyfi derinlikte
/// AND/OR ağacı, hem ayrıştırıcı hem de "bu sorgu ne kadar pahalı" tahmini
/// gerektirir. İki seviye, pratikteki filtrelerin neredeyse tamamını karşılıyor.
/// </summary>
public sealed record GatewayFilterGroup(IReadOnlyList<GatewayFilter> Any);

public enum GatewaySortDirection
{
    Asc,
    Desc,
}

/// <summary>
/// Bir ilişkinin gömülmesi (08 §2.1 <c>expand=</c>).
///
/// İlişkiyi ÇAĞIRAN bildirir, Gateway şemadan çıkarmaz. Sebep: Gateway durumsuz —
/// her istekte bağlantı dizesi alıyor, projenin şemasını bilmiyor. Şemayı sunucuda
/// aramak yalnızca API-anahtarı yolunda mümkün olurdu (orada proje belli), oturum
/// yolunda olmazdı; yalnızca bir kimlik yolunda çalışan bir özellik, hiç olmamasından
/// kötüdür. İstemci (Studio ya da üretilen SDK) şemayı zaten biliyor.
/// </summary>
/// <param name="FromColumn">Ana tablodaki yabancı anahtar kolonu.</param>
/// <param name="Table">Hedef tablo.</param>
/// <param name="ToColumn">Hedef tablodaki anahtar kolonu.</param>
/// <param name="As">Sonuçta hangi ad altında görüneceği. Boşsa hedef tablo adı.</param>
public sealed record GatewayExpand(string FromColumn, string Table, string ToColumn, string? As = null);

/// <param name="AffectedRows">Motorun bildirdiği etkilenen satır sayısı.</param>
/// <param name="Row">
/// Yazma sonrası satır — yalnızca motor bunu TEK ifadede güvenle döndürebiliyorsa
/// dolu olur (PostgreSQL/SQLite <c>RETURNING</c>). Diğer motorlarda null; bunu
/// "satır oluşmadı" diye okumak yanlış olur, <see cref="AffectedRows"/>'a bakılmalı.
/// </param>
public sealed record GatewayWriteResult(int AffectedRows, GatewayRow? Row);
