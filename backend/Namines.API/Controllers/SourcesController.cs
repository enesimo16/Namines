using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Namines.Core.Sources;

namespace Namines.API.Controllers;

/// <summary>
/// github/06-EKLENTI-MIMARISI.md — "+ Add source" menüsünün veri kaynağı.
///
/// <b>Neden liste sunucudan geliyor:</b> hangi kaynağın hangi grupta olduğunu
/// ve ne yapabildiğini istemcide tekrar yazmak, iki kopyanın ayrışması
/// demekti (model listesiyle aynı gerekçe, bkz. <c>QuotaController</c>'ın
/// model ucu). Menü kataloğu okuyor, kaynakları adıyla tanımıyor.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class SourcesController : ControllerBase
{
    private readonly ISchemaSourceCatalog _catalog;

    public SourcesController(ISchemaSourceCatalog catalog) => _catalog = catalog;

    /// <summary>
    /// Kaynak kataloğu.
    ///
    /// <b><c>kind</c> ve <c>capabilities</c> STRING olarak dönüyor</b>, enum
    /// sayısı olarak değil: sunucu enum'unu istemcide elle kopyalanmış bir
    /// birlik tipiyle eşlemek bu projede bir kez kırıldı (backend'e yeni bir
    /// değer eklendi, istemci kopyası güncellenmedi ve YANLIŞ değeri
    /// gösterdi). String sözleşmede tanınmayan bir değer yanlış değil,
    /// bilinmeyen olur — istemci onu güvenli tarafa koyabilir.
    /// </summary>
    [HttpGet]
    public IActionResult List() => Ok(_catalog.All().Select(s => new
    {
        id = s.Id,
        displayName = s.DisplayName,
        description = s.Description,
        kind = s.Kind.ToString().ToLowerInvariant(),
        capabilities = CapabilityNames(s.Capabilities),
        producesGuess = s.ProducesGuess,
    }));

    /// <summary>
    /// Bayrakları isimlere çevirir. <c>None</c> listeye girmez — "hiçbiri" bir
    /// yetenek değil, yeteneklerin yokluğu.
    /// </summary>
    private static IReadOnlyList<string> CapabilityNames(SchemaSourceCapability capabilities) =>
        Enum.GetValues<SchemaSourceCapability>()
            .Where(c => c != SchemaSourceCapability.None && capabilities.HasFlag(c))
            .Select(c => c.ToString().ToLowerInvariant())
            .ToArray();
}
