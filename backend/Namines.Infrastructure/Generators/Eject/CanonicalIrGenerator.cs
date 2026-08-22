using System.Collections.Generic;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Nsl;

namespace Namines.Infrastructure.Generators.Eject;

/// <summary>
/// <c>ir.json</c> — kanonik JSON ara temsili (04 §3).
///
/// <b>NSL hedefinin makine tarafı.</b> <c>nsl</c> insanın okuyup düzenlediği
/// biçim; bu ise başka araçların tükettiği biçim: CI'da doğrulanabilir,
/// diff'lenebilir, herhangi bir dilde okunabilir.
///
/// <c>nsl</c> gibi bu da ÇİFT YÖNLÜ — <see cref="CanonicalIr.Read"/> ile geri
/// okunabiliyor. Üretilen bir Django modelinden şemayı geri kurmanın yolu yok;
/// bu iki hedefte var.
/// </summary>
public sealed class CanonicalIrGenerator : IEjectGenerator
{
    public string Target => "ir.json";
    public string DisplayName => "Canonical schema IR (JSON)";

    public EjectResult Generate(DatabaseSchema schema, DatabaseType engine) =>
        new(new Dictionary<string, string> { ["ir.json"] = CanonicalIr.Write(schema, engine) },
            new List<string>());
}
