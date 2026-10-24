using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// PostgreSQL Row-Level Security (RLS) DDL üretim kuralları (04 §3 rls).
///
/// Tablo düzeyinde RLS'i etkinleştirir (ENABLE ROW LEVEL SECURITY) ve
/// süperkullanıcı / tablo sahibi ayrıcalıklarını sınırlamak için FORCE ROW LEVEL SECURITY uygular.
/// Tanımlı politikaları 'CREATE POLICY ... ON ... FOR ... TO ... USING ... WITH CHECK ...' olarak üretir.
///
/// Güvenlik sertleştirmesi (C-3):
/// 1. Command alanı whitelist kontrolünden geçirilir (ALL, SELECT, INSERT, UPDATE, DELETE).
/// 2. Role alanı ayrıştırılır ve standart pseudo-roller (PUBLIC, CURRENT_USER vb.) veya
///    güvenli SQL tanımlayıcıları dışındaki enjeksiyon girişimleri (noktalı virgül, yorum, geçersiz karakterler) reddedilir.
/// 3. Using ve WithCheck ifadelerinde çoklu SQL ifadesi çalıştırmayı (statement chaining) engellemek için
///    noktalı virgül (;) ve kontrol karakterleri kesinlikle yasaklanır.
/// </summary>
public static class RlsSql
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "ALL", "SELECT", "INSERT", "UPDATE", "DELETE"
    };

    private static readonly HashSet<string> PseudoRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "PUBLIC", "CURRENT_USER", "SESSION_USER", "CURRENT_ROLE"
    };

    private static readonly Regex ValidIdentifierRegex = new(@"^[a-zA-Z_][a-zA-Z0-9_$]*$", RegexOptions.Compiled);

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

                // Command doğrulaması: Yalnızca geçerli PostgreSQL RLS komutlarına izin verilir
                var rawCommand = string.IsNullOrWhiteSpace(policy.Command) ? "ALL" : policy.Command.Trim();
                if (!AllowedCommands.Contains(rawCommand))
                {
                    throw new ArgumentException($"Geçersiz RLS komutu '{policy.Command}'. Yalnızca ALL, SELECT, INSERT, UPDATE, DELETE desteklenir.", nameof(table));
                }
                var command = rawCommand.ToUpperInvariant();

                // Role doğrulaması: Virgülle ayrılmış roller doğrulanır, enjeksiyon önlenir
                var role = FormatAndValidateRoles(policy.Role, table.Name);

                // Using ve WithCheck ifadelerinde noktalı virgül (statement chaining) engeli
                ValidateRlsExpression(policy.Using, "USING", table.Name);
                ValidateRlsExpression(policy.WithCheck, "WITH CHECK", table.Name);

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

    private static string FormatAndValidateRoles(string? rawRole, string tableName)
    {
        if (string.IsNullOrWhiteSpace(rawRole)) return "PUBLIC";

        var parts = rawRole.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "PUBLIC";

        var validatedParts = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                throw new ArgumentException($"RLS rol listesi boş bir rol içeremez ({tableName}).", nameof(tableName));
            }

            if (part.Contains(';') || part.Contains("--") || part.Contains("/*"))
            {
                throw new ArgumentException($"RLS rol adı geçersiz karakter içeriyor: '{part}' ({tableName}).", nameof(tableName));
            }

            if (part.Any(char.IsControl))
            {
                throw new ArgumentException($"RLS rol adı kontrol karakteri içeremez: '{part}' ({tableName}).", nameof(tableName));
            }

            if (PseudoRoles.Contains(part))
            {
                validatedParts.Add(part.ToUpperInvariant());
            }
            else if (part.StartsWith('"') && part.EndsWith('"') && part.Length > 2)
            {
                var interior = part.Substring(1, part.Length - 2);
                if (interior.Contains('"') || interior.Contains(';'))
                {
                    throw new ArgumentException($"Alıntılanmış RLS rol adı geçersiz karakter içeriyor: '{part}' ({tableName}).", nameof(tableName));
                }
                validatedParts.Add(part);
            }
            else if (ValidIdentifierRegex.IsMatch(part))
            {
                validatedParts.Add(part);
            }
            else
            {
                throw new ArgumentException($"Geçersiz RLS rol adı: '{part}' ({tableName}).", nameof(tableName));
            }
        }

        return string.Join(", ", validatedParts);
    }

    private static void ValidateRlsExpression(string? expression, string clauseName, string tableName)
    {
        if (string.IsNullOrWhiteSpace(expression)) return;

        if (expression.Contains(';'))
        {
            throw new ArgumentException($"RLS {clauseName} ifadesi noktalı virgül (;) içeremez ({tableName}).", nameof(tableName));
        }

        if (expression.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
        {
            throw new ArgumentException($"RLS {clauseName} ifadesi geçersiz kontrol karakteri içeriyor ({tableName}).", nameof(tableName));
        }
    }
}
