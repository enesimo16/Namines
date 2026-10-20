using System;
using System.Text;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// PostgreSQL Row-Level Security (RLS) DDL üretim kuralları (04 §3 rls).
///
/// Tablo düzeyinde RLS'i etkinleştirir (ENABLE ROW LEVEL SECURITY) ve
/// süperkullanıcı / tablo sahibi ayrıcalıklarını sınırlamak için FORCE ROW LEVEL SECURITY uygular.
/// Tanımlı politikaları 'CREATE POLICY ... ON ... FOR ... TO ... USING ... WITH CHECK ...' olarak üretir.
/// </summary>
public static class RlsSql
{
    public static string GenerateTableRls(SchemaTable table, DatabaseType engine, Func<string, string> quote)
    {
        if (engine != DatabaseType.PostgreSQL) return string.Empty;

        bool hasPolicies = table.RlsPolicies != null && table.RlsPolicies.Count > 0;
        if (!table.EnableRls && !hasPolicies) return string.Empty;

        var sb = new StringBuilder();
        var quotedTable = quote(table.Name);

        sb.AppendLine($"ALTER TABLE {quotedTable} ENABLE ROW LEVEL SECURITY;");
        sb.AppendLine($"ALTER TABLE {quotedTable} FORCE ROW LEVEL SECURITY;");

        if (hasPolicies)
        {
            foreach (var policy in table.RlsPolicies!)
            {
                if (string.IsNullOrWhiteSpace(policy.Name)) continue;

                var quotedPolicy = quote(policy.Name);
                var command = string.IsNullOrWhiteSpace(policy.Command) ? "ALL" : policy.Command.ToUpperInvariant();
                var role = string.IsNullOrWhiteSpace(policy.Role) ? "PUBLIC" : policy.Role;

                sb.Append($"CREATE POLICY {quotedPolicy} ON {quotedTable} FOR {command} TO {role}");

                if (!string.IsNullOrWhiteSpace(policy.Using))
                {
                    sb.Append($" USING ({policy.Using.Trim()})");
                }

                if (!string.IsNullOrWhiteSpace(policy.WithCheck))
                {
                    sb.Append($" WITH CHECK ({policy.WithCheck.Trim()})");
                }

                sb.AppendLine(";");
            }
        }

        return sb.ToString();
    }
}
