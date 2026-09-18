using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Namines.API.Controllers;
using Namines.Core.Sources;

namespace Namines.Tests.Controllers;

/// <summary>github/06-EKLENTI-MIMARISI.md — katalog sınırı.</summary>
public class SourcesControllerTests
{
    private sealed class OneSourceCatalog : ISchemaSourceCatalog
    {
        public IReadOnlyList<SchemaSourceDescriptor> All() => new[]
        {
            new SchemaSourceDescriptor(
                "dbconnect", "Database connection", "Read the schema straight from a live database.",
                SchemaSourceKind.Connect,
                SchemaSourceCapability.Import | SchemaSourceCapability.Watch,
                ProducesGuess: false),
        };
    }

    [Fact]
    public void Kind_and_capabilities_cross_the_boundary_as_strings()
    {
        // Backend enum'unu istemcide sayıyla eşlemek bu projede bir kez
        // kırıldı: sunucuya yeni bir değer eklenince istemcideki birebir
        // kopya sessizce yanlış değeri gösterdi. Sözleşme string olursa
        // eklenen değer istemcide TANINMAZ olur — yanlış olmaz.
        var result = new SourcesController(new OneSourceCatalog()).List() as OkObjectResult;

        Assert.NotNull(result);
        var json = System.Text.Json.JsonSerializer.Serialize(result!.Value);

        Assert.Contains("\"kind\":\"connect\"", json);
        Assert.Contains("\"capabilities\":[\"import\",\"watch\"]", json);
        Assert.Contains("\"producesGuess\":false", json);
    }
}
