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

public enum GatewaySortDirection
{
    Asc,
    Desc,
}

/// <param name="AffectedRows">Motorun bildirdiği etkilenen satır sayısı.</param>
/// <param name="Row">
/// Yazma sonrası satır — yalnızca motor bunu TEK ifadede güvenle döndürebiliyorsa
/// dolu olur (PostgreSQL/SQLite <c>RETURNING</c>). Diğer motorlarda null; bunu
/// "satır oluşmadı" diye okumak yanlış olur, <see cref="AffectedRows"/>'a bakılmalı.
/// </param>
public sealed record GatewayWriteResult(int AffectedRows, GatewayRow? Row);
