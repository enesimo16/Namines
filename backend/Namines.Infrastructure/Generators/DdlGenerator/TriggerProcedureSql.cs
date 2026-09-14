using System.Linq;
using System.Text;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// Trigger/StoredProcedure'ları hedef motora göre GATE'leyerek DDL'e ekler.
///
/// Sözdizimi çevirisi YAPILMAZ — <see cref="SchemaTrigger.Body"/> ve
/// <see cref="SchemaStoredProcedure.Body"/> zaten seçilen hedef motor için
/// yazılmış ham SQL'dir (bkz. SchemaPromptBuilder motor-gated kural). Bu
/// sınıfın tek işi, o SQL'in yalnızca KENDİ motorunun çıktısına karışmasını
/// sağlamaktır — başka motorda üretilen DDL'e asla sızmaz.
/// </summary>
internal static class TriggerProcedureSql
{
    public static void Append(StringBuilder sb, DatabaseSchema schema, DatabaseType engine)
    {
        foreach (var trigger in schema.Triggers.Where(t => t.TargetEngine == engine))
        {
            if (string.IsNullOrWhiteSpace(trigger.Body)) continue;

            sb.AppendLine($"-- Trigger: {trigger.Id} ({trigger.Timing} {trigger.Event})");
            sb.AppendLine(trigger.Body.Trim());
            sb.AppendLine();
        }

        foreach (var proc in schema.StoredProcedures.Where(p => p.TargetEngine == engine))
        {
            if (string.IsNullOrWhiteSpace(proc.Body)) continue;

            sb.AppendLine($"-- Stored Procedure: {proc.Name}");
            sb.AppendLine(proc.Body.Trim());
            sb.AppendLine();
        }
    }
}
