using System;

namespace Namines.Infrastructure.Generators.Eject;

/// <summary>
/// Kanonik kolon tipinin dil-bağımsız sınıflandırması.
///
/// Her hedef ayrı ayrı <c>"INT" or "INTEGER" or "SMALLINT" …</c> listesi yazsaydı,
/// biri yeni bir tipi eklemeyi unuttuğunda o hedef sessizce "string" üretirdi —
/// ve bu, üretilen istemcide ancak çalışma zamanında görülürdü. Sınıflandırma tek
/// yerde yapılıp her hedef yalnızca kendi karşılığını seçiyor.
/// </summary>
internal enum TypeKind
{
    Integer,
    Long,
    Decimal,
    Double,
    Boolean,
    Date,
    Time,
    DateTime,
    Uuid,
    Json,
    Binary,
    Text,
}

internal static class CanonicalType
{
    public static TypeKind Classify(string? canonicalType) =>
        (canonicalType ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "INT" or "INTEGER" or "SMALLINT" or "TINYINT" or "SERIAL" => TypeKind.Integer,
            "BIGINT" or "BIGSERIAL" => TypeKind.Long,
            "DECIMAL" or "NUMERIC" or "MONEY" => TypeKind.Decimal,
            "FLOAT" or "REAL" or "DOUBLE" => TypeKind.Double,
            "BIT" or "BOOL" or "BOOLEAN" => TypeKind.Boolean,
            "DATE" => TypeKind.Date,
            "TIME" => TypeKind.Time,
            "DATETIME" or "DATETIME2" or "TIMESTAMP" or "TIMESTAMPTZ" => TypeKind.DateTime,
            "UUID" or "UNIQUEIDENTIFIER" => TypeKind.Uuid,
            "JSON" or "JSONB" => TypeKind.Json,
            "BINARY" or "VARBINARY" or "BLOB" or "IMAGE" or "BYTEA" => TypeKind.Binary,
            // Tanınmayan tip metne düşer. Uydurmak yerine en geniş tipe düşmek
            // doğru: yanlış bir sayısal tip, veriyi sessizce kırpabilir.
            _ => TypeKind.Text,
        };

    /// <summary>
    /// Tamsayı mı? Otomatik artan birincil anahtar kararı buna bağlı ve
    /// <c>Decimal</c>/<c>Double</c> ile karıştırılmamalı.
    /// </summary>
    public static bool IsIntegral(TypeKind kind) => kind is TypeKind.Integer or TypeKind.Long;
}
