using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Tests.Services;

/// <summary>
/// Hattı GERÇEK AI olmadan sürmek için. Her çağrıyı kaydeder; parça
/// çağrılarında istenen tabloları birebir üretir — hattın parçaları doğru
/// dağıtıp doğru birleştirdiğini bu sayede ölçebiliyoruz.
///
/// <b>Parça çağrıları BİLEREK asenkron (<see cref="Task.Yield"/>).</b> Bu bir
/// üslup tercihi değil, bir EMNİYET: eskiden bütün metotlar
/// <c>Task.FromResult</c> dönüyordu, yani hattın paralel parça döngüsü her
/// testte tek bir iş parçacığında SIRAYLA koşuyordu. Sonuç: paralel parçaların
/// tek bir SSE yanıtına eşzamanlı yazdığı kritik bir hata dokuz ayrı görev
/// incelemesinden geçti ve ancak bütünsel incelemede yakalandı — çünkü hiçbir
/// test iki parçayı gerçekten aynı anda çalıştırmıyordu.
///
/// Artık <see cref="DraftChunkAsync"/> devam etmeden önce teslim ediyor, böylece
/// bu sahteyi kullanan HER test (bugünküler ve gelecektekiler) gerçek
/// serpiştirmeyi ücretsiz olarak sınıyor. Bu yüzden aşağıdaki sayaç ve listeler
/// de iş parçacığı güvenli olmak ZORUNDA: emniyetin kendisi kırılgan bir teste
/// yol açarsa hiçbir işe yaramaz.
/// </summary>
public sealed class FakeSchemaDraftSource : ISchemaDraftSource
{
    private readonly object _recordLock = new();
    private readonly List<SchemaChunk> _chunkCalls = new();
    private readonly List<IReadOnlyList<string>> _chunkContexts = new();
    private int _draftCalls;
    private int _repairCalls;

    public string? PlanResponse { get; set; }

    /// <summary>
    /// Kaydedilen parça çağrıları — anlık bir KOPYA döner.
    ///
    /// Canlı listeyi vermek, çağrılar hâlâ sürerken numaralandıran bir testi
    /// <c>InvalidOperationException</c> ile düşürürdü.
    /// </summary>
    public IReadOnlyList<SchemaChunk> ChunkCalls
    {
        get { lock (_recordLock) return _chunkCalls.ToList(); }
    }

    public IReadOnlyList<IReadOnlyList<string>> ChunkContexts
    {
        get { lock (_recordLock) return _chunkContexts.ToList(); }
    }

    public int DraftCalls => Volatile.Read(ref _draftCalls);
    public int RepairCalls => Volatile.Read(ref _repairCalls);

    /// <summary>Etiketi burada olan parça çağrısı istisna fırlatır.</summary>
    public HashSet<string> FailingChunkLabels { get; } = new();

    public Task<string?> PlanAsync(string prompt, DatabaseType engine, CancellationToken ct = default)
        => Task.FromResult(PlanResponse);

    public Task<DatabaseSchema> DraftAsync(
        string prompt, DatabaseType engine, string? plan, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _draftCalls);
        return Task.FromResult(SchemaOf("fallback_table"));
    }

    public async Task<DatabaseSchema> DraftChunkAsync(
        string prompt, DatabaseType engine, SchemaChunk chunk,
        IReadOnlyList<string> allTableNames, CancellationToken ct = default)
    {
        // Gerçek serpiştirmeyi zorlayan satır — sınıf doc'undaki gerekçeye bakın.
        await Task.Yield();

        lock (_recordLock)
        {
            _chunkCalls.Add(chunk);
            _chunkContexts.Add(allTableNames);
        }

        if (FailingChunkLabels.Contains(chunk.Label))
            throw new InvalidOperationException($"chunk '{chunk.Label}' failed");

        return SchemaOf(chunk.OwnedTables.ToArray());
    }

    public Task<DatabaseSchema> RepairAsync(
        DatabaseSchema schema, IReadOnlyList<string> findings,
        DatabaseType engine, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _repairCalls);
        return Task.FromResult(schema);
    }

    /// <summary>0 = "tavan bilinmiyor" — çağıran bugünkü sabit eşik/hedefe düşer.</summary>
    public int? EffectiveMaxOutputTokens { get; set; }

    public Task<int> EffectiveMaxOutputTokensAsync(CancellationToken ct = default) =>
        Task.FromResult(EffectiveMaxOutputTokens ?? 0);

    /// <summary>
    /// Her tabloya bir birincil anahtar veriliyor: aksi hâlde NslValidator
    /// bulgu üretir, hat onarım döngüsüne girer ve test ölçmek istediği
    /// şeyi değil, döngüyü ölçer.
    /// </summary>
    private static DatabaseSchema SchemaOf(params string[] tableNames)
    {
        var schema = new DatabaseSchema { Name = "Fake" };
        foreach (var name in tableNames)
        {
            schema.Tables.Add(new SchemaTable
            {
                Id = SchemaIdConvention.TableId(name),
                Name = name,
                Columns = new List<SchemaColumn>
                {
                    new()
                    {
                        Id = SchemaIdConvention.ColumnId(name, "id"),
                        Name = "id", Type = "uuid", IsPK = true, IsNullable = false,
                    },
                },
            });
        }
        return schema;
    }
}
