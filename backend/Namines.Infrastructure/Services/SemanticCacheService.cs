using Microsoft.Extensions.Caching.Memory;
using Namines.Core.Models;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Namines.Infrastructure.Services
{
    public class SemanticCacheService
    {
        private readonly IMemoryCache _memoryCache;

        public SemanticCacheService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        public Task<string?> TryGetAsync(string endpointTag, DatabaseSchema schema, object? additionalParams, string? tenantScope = null)
        {
            var cacheKey = ComputeCacheKey(endpointTag, schema, additionalParams, tenantScope);
            if (_memoryCache.TryGetValue(cacheKey, out string? cachedResponse))
            {
                return Task.FromResult(cachedResponse);
            }
            return Task.FromResult<string?>(null);
        }

        public Task SetAsync(string endpointTag, DatabaseSchema schema, object? additionalParams, string responseJson, TimeSpan? ttl = null, string? tenantScope = null)
        {
            var cacheKey = ComputeCacheKey(endpointTag, schema, additionalParams, tenantScope);
            var cacheEntryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromHours(1)
            };
            _memoryCache.Set(cacheKey, responseJson, cacheEntryOptions);
            return Task.CompletedTask;
        }

        private string ComputeCacheKey(string endpointTag, DatabaseSchema schema, object? additionalParams, string? tenantScope)
        {
            var scope = string.IsNullOrWhiteSpace(tenantScope) ? "anon" : tenantScope.Trim().Replace(':', '_');
            var schemaHash = ComputeSchemaHash(schema);
            var paramHash = ComputeParamHash(additionalParams);
            return $"namines:ai:{scope}:{endpointTag}:{schemaHash}:{paramHash}";
        }

        private string ComputeSchemaHash(DatabaseSchema schema)
        {
            if (schema == null) return "null-schema";

            // SEC-01: Normalize full schema (including Indexes, Uniques, Checks, Enums,
            // nullability, defaults, identity, and enum references) so adding an index or constraint
            // invalidates the previous cache entry deterministically.
            var normalizedTables = schema.Tables?
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .Select(t => new
                {
                    t.Name,
                    t.EnableRls,
                    Columns = t.Columns?
                        .OrderBy(c => c.Name, StringComparer.Ordinal)
                        .Select(c => new
                        {
                            c.Name,
                            c.Type,
                            c.Length,
                            c.Scale,
                            c.IsPK,
                            c.IsFK,
                            c.IsNullable,
                            c.Identity,
                            c.DefaultValue,
                            c.EnumRef,
                            c.Generated,
                            c.Collation,
                            c.IsArray
                        })
                        .ToList(),
                    Indexes = t.Indexes?
                        .OrderBy(i => i.Name ?? string.Empty, StringComparer.Ordinal)
                        .Select(i => new
                        {
                            i.Name,
                            i.IsUnique,
                            i.Where,
                            i.Method,
                            Columns = i.Columns != null
                                ? string.Join(",", i.Columns.Select(col => $"{col.ColumnId}:{(col.Descending ? "DESC" : "ASC")}"))
                                : "",
                            Include = i.IncludeColumnIds != null
                                ? string.Join(",", i.IncludeColumnIds)
                                : ""
                        })
                        .ToList(),
                    Uniques = t.Uniques?
                        .OrderBy(u => u.Name ?? string.Empty, StringComparer.Ordinal)
                        .Select(u => new
                        {
                            u.Name,
                            Columns = u.ColumnIds != null ? string.Join(",", u.ColumnIds) : ""
                        })
                        .ToList(),
                    Checks = t.Checks?
                        .OrderBy(ck => ck.Name ?? string.Empty, StringComparer.Ordinal)
                        .Select(ck => new
                        {
                            ck.Name,
                            ck.Expression
                        })
                        .ToList()
                })
                .ToList();

            var normalizedEnums = schema.Enums?
                .OrderBy(e => e.Name, StringComparer.Ordinal)
                .Select(e => new
                {
                    e.Name,
                    Values = e.Values != null ? string.Join(",", e.Values) : ""
                })
                .ToList();

            var normalizedRelations = schema.Relations?
                .OrderBy(r => $"{r.SourceTableId}.{r.SourceColumnId}->{r.TargetTableId}.{r.TargetColumnId}", StringComparer.Ordinal)
                .Select(r => new
                {
                    r.SourceTableId,
                    r.SourceColumnId,
                    r.TargetTableId,
                    r.TargetColumnId,
                    r.OnDelete,
                    r.OnUpdate
                })
                .ToList();

            var canonicalSchema = new
            {
                Tables = normalizedTables,
                Enums = normalizedEnums,
                Relations = normalizedRelations
            };

            var json = JsonSerializer.Serialize(canonicalSchema);
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(bytes);
        }

        private string ComputeParamHash(object? additionalParams)
        {
            if (additionalParams == null) return "no-params";
            var json = JsonSerializer.Serialize(additionalParams);
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(bytes);
        }
    }
}
