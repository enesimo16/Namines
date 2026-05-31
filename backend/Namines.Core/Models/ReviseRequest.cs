using System.Collections.Generic;

namespace Namines.Core.Models;

public class ReviseRequest
{
    public string RevisionPrompt { get; set; } = string.Empty;
    public List<SchemaTable> SelectedTables { get; set; } = new();
    public List<SchemaRelation> ExistingRelations { get; set; } = new();
    public string AIProvider { get; set; } = "Groq";
    public string ModelName { get; set; } = string.Empty;
}
