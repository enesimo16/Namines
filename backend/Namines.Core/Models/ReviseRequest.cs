using System.Collections.Generic;

namespace Namines.Core.Models;

public class ReviseRequest
{
    public string RevisionPrompt { get; set; } = string.Empty;
    public List<SchemaTable> SelectedTables { get; set; } = new();
    public List<SchemaRelation> ExistingRelations { get; set; } = new();
    public string AIProvider { get; set; } = "Groq";
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// Şemanın tetikleyicileri. Modelin bunları görmesi şart: göremediği bir
    /// trigger'ı düzeltemez ve motor uyuşmazlığı bulgusu (NSL024) hiçbir zaman
    /// kapanmaz — döngü bütçeyi boşa harcar.
    /// </summary>
    public List<SchemaTrigger> Triggers { get; set; } = new();

    /// <summary>Saklı yordamlar — <see cref="Triggers"/> ile aynı gerekçe.</summary>
    public List<SchemaStoredProcedure> StoredProcedures { get; set; } = new();

    /// <summary>Enum tipleri — <see cref="Triggers"/> ile aynı gerekçe.</summary>
    public List<SchemaEnum> Enums { get; set; } = new();
}
