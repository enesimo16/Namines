using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.Scaffold;

/// <summary>
/// TypeScript istemci SDK'si: tipler, Zod semalari, TanStack Query kancalari.
///
/// <see cref="Namines.Infrastructure.Services.ScaffolderService"/>'ten ayrildi
/// (ARCH-003 / B-37): 1.760 satirlik tek dosya, hedef basina bir uretece
/// bolundu -- <c>Generators/Eject/</c> altindaki mevcut desenle ayni.
/// Metot govdeleri DEGISMEDI; <c>ScaffolderSnapshotTests</c> ciktinin bayt
/// bayt ayni kaldigini kanitliyor.
/// </summary>
internal static class FrontendSdkScaffold
{
    internal static string GenerateFrontendTypes(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"export interface {table.Name} {{");
            foreach (var col in table.Columns)
            {
                var tsType = MapSqlToTsType(col.Type);
                var isOptional = col.IsNullable ? "?" : "";
                sb.AppendLine($"  {col.Name.ToLowerInvariant()}{isOptional}: {tsType};");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    internal static string GenerateFrontendZod(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("import { z } from 'zod';");
        sb.AppendLine();
        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"export const {table.Name}Schema = z.object({{");
            foreach (var col in table.Columns)
            {
                var zodDef = MapSqlToZod(col.Type, col.IsNullable);
                sb.AppendLine($"  {col.Name.ToLowerInvariant()}: {zodDef},");
            }
            sb.AppendLine("});");
            sb.AppendLine();
            sb.AppendLine($"export type {table.Name}Input = z.infer<typeof {table.Name}Schema>;");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    internal static string GenerateFrontendHooks(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';");
        sb.AppendLine("import axios from 'axios';");
        sb.AppendLine("import * as T from './types';");
        sb.AppendLine();
        sb.AppendLine("const API_BASE = '/api';");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"// ─── {table.Name} Hooks ──────────────────────────────────────────");
            sb.AppendLine();
            
            // Get All
            sb.AppendLine($"export function useGet{table.Name}s() {{");
            sb.AppendLine("  return useQuery<T." + table.Name + "[]>({");
            sb.AppendLine($"    queryKey: ['{table.Name}s'],");
            sb.AppendLine($"    queryFn: async () => {{");
            sb.AppendLine($"      const res = await axios.get(`${{API_BASE}}/{table.Name}`);");
            sb.AppendLine("      return res.data;");
            sb.AppendLine("    }");
            sb.AppendLine("  });");
            sb.AppendLine("}");
            sb.AppendLine();

            // Get By Id
            sb.AppendLine($"export function useGet{table.Name}(id: any) {{");
            sb.AppendLine("  return useQuery<T." + table.Name + ">({");
            sb.AppendLine($"    queryKey: ['{table.Name}', id],");
            sb.AppendLine($"    queryFn: async () => {{");
            sb.AppendLine($"      const res = await axios.get(`${{API_BASE}}/{table.Name}/${{id}}`);");
            sb.AppendLine("      return res.data;");
            sb.AppendLine("    },");
            sb.AppendLine("    enabled: id !== undefined && id !== null");
            sb.AppendLine("  });");
            sb.AppendLine("}");
            sb.AppendLine();

            // Create
            sb.AppendLine($"export function useCreate{table.Name}() {{");
            sb.AppendLine("  const queryClient = useQueryClient();");
            sb.AppendLine("  return useMutation<T." + table.Name + ", Error, T." + table.Name + ">({");
            sb.AppendLine($"    mutationFn: async (data) => {{");
            sb.AppendLine($"      const res = await axios.post(`${{API_BASE}}/{table.Name}`, data);");
            sb.AppendLine("      return res.data;");
            sb.AppendLine("    },");
            sb.AppendLine("    onSuccess: () => {");
            sb.AppendLine($"      queryClient.invalidateQueries({{ queryKey: ['{table.Name}s'] }});");
            sb.AppendLine("    }");
            sb.AppendLine("  });");
            sb.AppendLine("}");
            sb.AppendLine();

            // Update
            sb.AppendLine($"export function useUpdate{table.Name}(id: any) {{");
            sb.AppendLine("  const queryClient = useQueryClient();");
            sb.AppendLine("  return useMutation<void, Error, T." + table.Name + ">({");
            sb.AppendLine($"    mutationFn: async (data) => {{");
            sb.AppendLine($"      await axios.put(`${{API_BASE}}/{table.Name}/${{id}}`, data);");
            sb.AppendLine("    },");
            sb.AppendLine("    onSuccess: () => {");
            sb.AppendLine($"      queryClient.invalidateQueries({{ queryKey: ['{table.Name}s'] }});");
            sb.AppendLine($"      queryClient.invalidateQueries({{ queryKey: ['{table.Name}', id] }});");
            sb.AppendLine("    }");
            sb.AppendLine("  });");
            sb.AppendLine("}");
            sb.AppendLine();

            // Delete
            sb.AppendLine($"export function useDelete{table.Name}() {{");
            sb.AppendLine("  const queryClient = useQueryClient();");
            sb.AppendLine("  return useMutation<void, Error, any>({");
            sb.AppendLine($"    mutationFn: async (id) => {{");
            sb.AppendLine($"      await axios.delete(`${{API_BASE}}/{table.Name}/${{id}}`);");
            sb.AppendLine("    },");
            sb.AppendLine("    onSuccess: () => {");
            sb.AppendLine($"      queryClient.invalidateQueries({{ queryKey: ['{table.Name}s'] }});");
            sb.AppendLine("    }");
            sb.AppendLine("  });");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    internal static string MapSqlToTsType(string sqlType)
    {
        var type = sqlType.ToUpperInvariant();
        if (type.Contains("INT") || type.Contains("INTEGER") || type.Contains("DECIMAL") || type.Contains("MONEY") || type.Contains("FLOAT") || type.Contains("DOUBLE"))
            return "number";
        if (type.Contains("BIT") || type.Contains("BOOL"))
            return "boolean";
        return "string";
    }

    internal static string MapSqlToZod(string sqlType, bool isNullable)
    {
        var tsType = MapSqlToTsType(sqlType);
        string zodDef;

        if (tsType == "number")
        {
            if (sqlType.ToUpperInvariant().Contains("INT"))
                zodDef = "z.number().int()";
            else
                zodDef = "z.number()";
        }
        else if (tsType == "boolean")
        {
            zodDef = "z.boolean()";
        }
        else
        {
            if (sqlType.ToUpperInvariant().Contains("UUID") || sqlType.ToUpperInvariant().Contains("UNIQUEIDENTIFIER"))
                zodDef = "z.string().uuid()";
            else
                zodDef = "z.string()";
        }

        if (isNullable)
        {
            zodDef += ".nullable().optional()";
        }

        return zodDef;
    }
}
