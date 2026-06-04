using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DocumentationGenerator;

public class DocumentationGeneratorService : IDocumentationGenerator
{
    private readonly PdfReportGenerator _pdfGenerator;
    private readonly MermaidErGenerator _mermaidGenerator;
    private readonly ReadmeGenerator _readmeGenerator;

    public DocumentationGeneratorService()
    {
        _pdfGenerator = new PdfReportGenerator();
        _mermaidGenerator = new MermaidErGenerator();
        _readmeGenerator = new ReadmeGenerator();
    }

    public byte[] GeneratePdf(DatabaseSchema schema, string projectSummary, string language = "tr")
    {
        return _pdfGenerator.Generate(schema, projectSummary, language);
    }

    public string GenerateMermaidEr(DatabaseSchema schema)
    {
        return _mermaidGenerator.Generate(schema);
    }

    public string GenerateReadme(DatabaseSchema schema, string language = "tr")
    {
        return _readmeGenerator.Generate(schema, language);
    }
}
