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

        public Task<string?> TryGetAsync(string endpointTag, DatabaseSchema schema, object? additionalParams)
        {
            var cacheKey = ComputeCacheKey(endpointTag, schema, additionalParams);
            if (_memoryCache.TryGetValue(cacheKey, out string? cachedResponse))
            {
                return Task.FromResult(cachedResponse);
            }
            return Task.FromResult<string?>(null);
        }

        public Task SetAsync(string endpointTag, DatabaseSchema schema, object? additionalParams, string responseJson, TimeSpan? ttl = null)
        {
            var cacheKey = ComputeCacheKey(endpointTag, schema, additionalParams);
            var cacheEntryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromHours(1)
            };
            _memoryCache.Set(cacheKey, responseJson, cacheEntryOptions);
            return Task.CompletedTask;
        }

        private string ComputeCacheKey(string endpointTag, DatabaseSchema schema, object? additionalParams)
        {
            var schemaHash = ComputeSchemaHash(schema);
            var paramHash = ComputeParamHash(additionalParams);
            return $"namines:ai:{endpointTag}:{schemaHash}:{paramHash}";
        }

        private string ComputeSchemaHash(DatabaseSchema schema)
        {
            if (schema == null) return "null-schema";

            // Normalize schema: sort tables, columns, and relations to make caching order-independent
            var normalizedTables = schema.Tables?
                .OrderBy(t => t.Name)
                .Select(t => new
                {
                    t.Name,
                    Columns = t.Columns?
                        .OrderBy(c => c.Name)
                        .Select(c => new { c.Name, c.Type, c.IsPK, c.IsFK })
                        .ToList()
                })
                .ToList();

            var normalizedRelations = schema.Relations?
                .OrderBy(r => $"{r.SourceTableId}.{r.SourceColumnId}->{r.TargetTableId}.{r.TargetColumnId}")
                .Select(r => new { r.SourceTableId, r.SourceColumnId, r.TargetTableId, r.TargetColumnId })
                .ToList();

            var normalized = new
            {
                Tables = normalizedTables,
                Relations = normalizedRelations
            };

            var json = JsonSerializer.Serialize(normalized);
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(hashBytes).ToLower();
        }

        private string ComputeParamHash(object? additionalParams)
        {
            if (additionalParams == null) return "no-params";
            var json = JsonSerializer.Serialize(additionalParams);
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return Convert.ToHexString(hashBytes).ToLower();
        }
    }
}
