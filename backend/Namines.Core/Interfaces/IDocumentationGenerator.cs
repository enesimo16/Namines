using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface IDocumentationGenerator
{
    byte[] GeneratePdf(DatabaseSchema schema, string projectSummary, string language = "tr");
    string GenerateMermaidEr(DatabaseSchema schema);
    string GenerateReadme(DatabaseSchema schema);
}
