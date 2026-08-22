using System.Collections.Generic;
using System.Linq;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Nsl;

namespace Namines.Infrastructure.Generators.Eject;

/// <summary>
/// <c>nsl</c> — şemanın kendi metin biçimi (new-phase/04-NSL-SCHEMA-IR.md).
///
/// Diğer eject hedeflerinden farkı: bu ÇIFT YÖNLÜ. Üretilen dosya geri okunabilir
/// (<see cref="NslParser"/>), yani kullanıcı şemasını git'te tutup düzenleyebilir
/// ve Namines'e geri getirebilir. Diğer hedefler tek yönlüdür — üretilen Django
/// modelinden şemayı geri kurmanın yolu yok.
///
/// Doğrulama bulguları uyarı olarak veriliyor: dosya zaten üretiliyor, ama
/// şemadaki sorunları görmek için ayrı bir ekrana gitmek gerekmesin.
/// </summary>
public sealed class NslGenerator : IEjectGenerator
{
    public string Target => "nsl";
    public string DisplayName => "NSL schema file";

    public EjectResult Generate(DatabaseSchema schema, DatabaseType engine)
    {
        var warnings = NslValidator.Validate(schema, engine)
            // Bilgi seviyesindekiler uyarı listesine girmiyor: "bu tablo yalnız"
            // gibi notlar, gerçek sorunların görülmesini zorlaştırırdı.
            .Where(f => f.Severity is "error" or "warning")
            .Select(f => $"{f.Code} [{f.Severity}] {Where(f)}{f.Message}")
            .ToList();

        return new EjectResult(
            new Dictionary<string, string> { ["schema.nsl"] = NslWriter.Write(schema, engine) },
            warnings);
    }

    private static string Where(NslFinding finding) =>
        finding.Table is null ? string.Empty
        : finding.Column is null ? $"{finding.Table}: "
        : $"{finding.Table}.{finding.Column}: ";
}
