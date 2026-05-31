using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Models;

namespace Namines.Infrastructure.Services
{
    public static class ProgrammaticSchemaOptimizer
    {
        public static DatabaseSchema Optimize(List<SchemaTable> selectedTables, List<SchemaRelation> existingRelations, string revisionPrompt)
        {
            var result = new DatabaseSchema
            {
                SchemaId = Guid.NewGuid().ToString(),
                Name = "Optimized Schema",
                Tables = new List<SchemaTable>(),
                Relations = new List<SchemaRelation>(existingRelations ?? new())
            };

            if (selectedTables == null) return result;

            foreach (var originalTable in selectedTables)
            {
                // Deep copy table
                var table = new SchemaTable
                {
                    Id = originalTable.Id,
                    Name = originalTable.Name,
                    StableUuid = originalTable.StableUuid,
                    Columns = new List<SchemaColumn>()
                };

                foreach (var originalCol in originalTable.Columns)
                {
                    table.Columns.Add(new SchemaColumn
                    {
                        Id = originalCol.Id,
                        Name = originalCol.Name,
                        StableUuid = originalCol.StableUuid,
                        Type = originalCol.Type,
                        Length = originalCol.Length,
                        IsPK = originalCol.IsPK,
                        IsFK = originalCol.IsFK,
                        IsNullable = originalCol.IsNullable,
                        DefaultValue = originalCol.DefaultValue
                    });
                }

                result.Tables.Add(table);
            }

            // 1. Primary Key Check and Optimization
            foreach (var table in result.Tables)
            {
                bool hasPk = table.Columns.Any(c => c.IsPK);
                if (!hasPk)
                {
                    // Try to find an existing column to make PK
                    var pkCandidate = table.Columns.FirstOrDefault(c => 
                        c.Name.Equals("id", StringComparison.OrdinalIgnoreCase) || 
                        c.Name.Equals($"{table.Name}id", StringComparison.OrdinalIgnoreCase) || 
                        c.Name.Equals($"{table.Name}_id", StringComparison.OrdinalIgnoreCase)
                    );

                    if (pkCandidate != null)
                    {
                        pkCandidate.IsPK = true;
                        pkCandidate.IsNullable = false;
                    }
                    else
                    {
                        // Insert a standard auto-increment primary key 'id' at index 0
                        var newPk = new SchemaColumn
                        {
                            Id = Guid.NewGuid().ToString(),
                            Name = "id",
                            Type = "int",
                            IsPK = true,
                            IsNullable = false,
                            Length = null,
                            StableUuid = Guid.NewGuid().ToString()
                        };
                        table.Columns.Insert(0, newPk);
                    }
                }
            }

            // 2. Size / FinOps Optimization (NVARCHAR(MAX) etc.)
            foreach (var table in result.Tables)
            {
                foreach (var col in table.Columns)
                {
                    var typeLower = col.Type.ToLower();
                    
                    // Check if it's a string type
                    bool isStringType = typeLower.Contains("varchar") || typeLower.Contains("char") || 
                                         typeLower.Contains("text") || typeLower.Contains("string");

                    if (isStringType)
                    {
                        // If it has "max" in type name, clean it up and set safe length
                        if (typeLower.Contains("max"))
                        {
                            col.Type = col.Type.Replace("(max)", "").Replace("MAX", "").Trim();
                            col.Length = 255;
                        }
                        // If length is null or <= 0, give a sensible limit (FinOps optimization)
                        else if (col.Length == null || col.Length <= 0)
                        {
                            if (typeLower.Contains("text"))
                            {
                                col.Length = 1000;
                            }
                            else
                            {
                                col.Length = 255;
                            }
                        }
                    }
                }
            }

            // 3. Foreign Key / Relationship Optimization
            foreach (var table in result.Tables)
            {
                foreach (var col in table.Columns)
                {
                    // If column looks like a foreign key (ends with Id/Id_ and not the primary key of this table)
                    if (!col.IsPK && (col.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) || col.Name.EndsWith("_id", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Ensure IsFK is marked
                        col.IsFK = true;

                        if (string.IsNullOrEmpty(col.DefaultValue))
                        {
                            col.DefaultValue = "/* Indexed */";
                        }
                        else if (!col.DefaultValue.Contains("Indexed"))
                        {
                            col.DefaultValue += " & Indexed";
                        }

                        // Try to find the target table
                        string targetPrefix = col.Name.Substring(0, col.Name.Length - (col.Name.EndsWith("Id") ? 2 : 3));
                        
                        // Look for a table that matches the prefix, or is pluralized/singularized match
                        var targetTable = result.Tables.FirstOrDefault(t => 
                            t.Name.Equals(targetPrefix, StringComparison.OrdinalIgnoreCase) ||
                            t.Name.Equals(targetPrefix + "s", StringComparison.OrdinalIgnoreCase) ||
                            (targetPrefix.EndsWith("y") && t.Name.Equals(targetPrefix.Substring(0, targetPrefix.Length - 1) + "ies", StringComparison.OrdinalIgnoreCase))
                        );

                        if (targetTable != null)
                        {
                            // Find target primary key
                            var targetPk = targetTable.Columns.FirstOrDefault(c => c.IsPK) 
                                           ?? targetTable.Columns.FirstOrDefault(c => c.Name.Equals("id", StringComparison.OrdinalIgnoreCase));

                            if (targetPk != null)
                            {
                                // Check if relationship already exists
                                bool relExists = result.Relations.Any(r => 
                                    (r.SourceTableId == table.Id && r.SourceColumnId == col.Id && r.TargetTableId == targetTable.Id) ||
                                    (r.TargetTableId == table.Id && r.TargetColumnId == col.Id && r.SourceTableId == targetTable.Id)
                                );

                                if (!relExists)
                                {
                                    result.Relations.Add(new SchemaRelation
                                    {
                                        Id = Guid.NewGuid().ToString(),
                                        Type = "1:N",
                                        SourceTableId = targetTable.Id,
                                        SourceColumnId = targetPk.Id,
                                        TargetTableId = table.Id,
                                        TargetColumnId = col.Id
                                    });
                                }
                            }
                        }
                    }
                }
            }

            // 4. Data Encryption / Masking for Sensitive Fields
            var sensitiveKeywords = new[] { "password", "sifre", "creditcard", "kredit", "kart", "cc", "ssn", "tcno", "tckn", "iban", "cvv", "secret", "gizli", "pin", "hash" };
            foreach (var table in result.Tables)
            {
                foreach (var col in table.Columns)
                {
                    string colNameLower = col.Name.ToLower();
                    if (sensitiveKeywords.Any(k => colNameLower.Contains(k)))
                    {
                        // Ensure password/sensitive hashes are stored in nvarchar with sufficient length
                        var typeLower = col.Type.ToLower();
                        if (typeLower.Contains("varchar") || typeLower.Contains("string") || typeLower.Contains("char"))
                        {
                            if (col.Length == null || col.Length < 128)
                            {
                                col.Length = 255; // Secure hash length
                            }
                        }

                        // Add annotation or default value to indicate encryption mapping
                        if (string.IsNullOrEmpty(col.DefaultValue))
                        {
                            col.DefaultValue = "/* Secured/Encrypted */";
                        }
                    }
                }
            }

            return result;
        }
    }
}
