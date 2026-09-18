using System.Collections.Generic;
using Namines.Core.Sources;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Bugünkü kaynaklar. Liste sabit çünkü kaynaklar koda gömülü — kullanıcı
/// başına değişen bir şey yok. Kullanıcıya göre değişen ilk kaynak (bağlı
/// GitHub deposu) F1'de geldiğinde bu sınıf onu ayrı bir yoldan ekleyecek.
/// </summary>
public sealed class StaticSchemaSourceCatalog : ISchemaSourceCatalog
{
    private static readonly IReadOnlyList<SchemaSourceDescriptor> Sources = new[]
    {
        // Menüde ilk sırada: ürünün bugün en çok anlatılan girişi.
        // Grup "import", çünkü F1 tek seferlik okuma yapıyor; drift takibi
        // (Watch) F3'te gelince "connect"e terfi edecek.
        new SchemaSourceDescriptor(
            "github", "GitHub repository",
            "Read the schema out of a public repository's code.",
            SchemaSourceKind.Import,
            SchemaSourceCapability.Import | SchemaSourceCapability.Compare,
            ProducesGuess: false),

        new SchemaSourceDescriptor(
            "dbconnect", "Database connection",
            "Read the schema straight from a live database.",
            SchemaSourceKind.Connect,
            SchemaSourceCapability.Import | SchemaSourceCapability.Compare | SchemaSourceCapability.Watch,
            ProducesGuess: false),

        new SchemaSourceDescriptor(
            "openapi", "OpenAPI / GraphQL URL",
            "Infer a data model from an API specification.",
            SchemaSourceKind.Import,
            SchemaSourceCapability.Import | SchemaSourceCapability.Compare,
            ProducesGuess: true),

        new SchemaSourceDescriptor(
            "code", "Code files",
            "Prisma schema, EF Core entities or raw SQL.",
            SchemaSourceKind.Import,
            SchemaSourceCapability.Import | SchemaSourceCapability.Compare,
            ProducesGuess: false),

        new SchemaSourceDescriptor(
            "jsonshape", "Sample JSON response",
            "Infer entities from the shape of API responses.",
            SchemaSourceKind.Import,
            SchemaSourceCapability.Import,
            ProducesGuess: true),

        new SchemaSourceDescriptor(
            "image", "Image",
            "Read a diagram or screenshot of a schema.",
            SchemaSourceKind.Import,
            SchemaSourceCapability.Import,
            ProducesGuess: true),

        new SchemaSourceDescriptor(
            "starter", "Starter schemas",
            "Five ready-made schemas to begin from.",
            SchemaSourceKind.Starter,
            SchemaSourceCapability.Import,
            ProducesGuess: false),
    };

    public IReadOnlyList<SchemaSourceDescriptor> All() => Sources;
}
