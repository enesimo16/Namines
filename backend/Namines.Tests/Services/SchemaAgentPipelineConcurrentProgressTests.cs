using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

/// <summary>
/// Final whole-branch review C1: paralel parça (chunk) üretimi
/// <c>progress.Report</c>'u birden çok arka plan görevinden AYNI ANDA
/// çağırabiliyordu. İki bileşik kusur vardı:
///
/// 1. <see cref="AsyncProgress{T}"/>'in kendi belgelediği "sırayla ve
///    tamamlanmadan bir sonrakine geçmeden" sözü yalnızca TEK bir çağıran
///    thread'i içindi — birden çok thread aynı anda <c>Report</c>'u
///    çağırdığında callback'in içine eş zamanlı girilebiliyordu.
/// 2. <c>SchemaAgentPipeline</c>'da <c>progress?.Report(...)</c> çağrısı,
///    başarısız bir parçayı "[merge] Domain 'X' failed" notuna çeviren
///    <c>catch</c>'in İÇİNDEYDİ — yani raporlama patlarsa, zaten BAŞARIYLA
///    üretilmiş (ve kullanıcının bedelini ödediği) bir parça yanlış sebeple
///    çöpe gidiyordu.
///
/// Bu test dosyası her ikisini de, GERÇEKTEN eş zamanlı çalışan iki parça
/// çağrısıyla (fake, <c>Task.FromResult</c> DEĞİL — bir kapı/gate ile ikisi
/// de aynı anda "uçuşta" olmaya zorlanıyor) doğruluyor.
/// </summary>
public class SchemaAgentPipelineConcurrentProgressTests
{
    // 14 tablo, iki parça (bkz. SchemaAgentPipelinePartitionTests) — ikisi de
    // paralel Task.WhenAll içinde çalışacak.
    private const string LargePlanJson = """
        {
          "schemaName": "shop",
          "domains": [
            { "name": "Identity", "tables": ["users", "roles", "permissions", "sessions"] },
            { "name": "Catalog", "tables": ["products", "categories", "suppliers", "reviews"] },
            { "name": "Orders", "tables": [
              "orders", "order_items", "payments", "shipments", "carts", "coupons"
            ] }
          ]
        }
        """;

    private static SchemaAgentPipeline Pipeline(ISchemaDraftSource source) =>
        new(source, new DdlGeneratorFactory(), NullLogger<SchemaAgentPipeline>.Instance);

    /// <summary>
    /// GERÇEKTEN asenkron sahte kaynak: her <see cref="DraftChunkAsync"/>
    /// çağrısı, TÜM parçalar "uçuşa" gelene kadar bir <see cref="TaskCompletionSource"/>
    /// üzerinde askıda kalır, sonra hepsi birlikte devam eder. Bu, sahte
    /// dünyadaki her lambda'nın <c>Task.FromResult</c> ile tek thread'de art
    /// arda tamamlandığı (ve bu yüzden hiçbir zaman gerçekten çakışmadığı)
    /// eski testlerin göremediği yarışı üretiyor.
    /// </summary>
    private sealed class GatedAsyncSchemaDraftSource : ISchemaDraftSource
    {
        private readonly TaskCompletionSource _allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        private readonly int _expectedChunks;

        public GatedAsyncSchemaDraftSource(int expectedChunks) => _expectedChunks = expectedChunks;

        public Task<string?> PlanAsync(string prompt, DatabaseType engine, CancellationToken ct = default) =>
            Task.FromResult<string?>(LargePlanJson);

        public Task<DatabaseSchema> DraftAsync(
            string prompt, DatabaseType engine, string? plan, CancellationToken ct = default) =>
            throw new InvalidOperationException("bu test tekil taslak yolunu kullanmıyor");

        public async Task<DatabaseSchema> DraftChunkAsync(
            string prompt, DatabaseType engine, SchemaChunk chunk,
            IReadOnlyList<string> allTableNames, CancellationToken ct = default)
        {
            // Her çağrı kendi varışını bildirir; SON çağrı kapıyı açar ve
            // hepsi (bu an itibarıyla gerçekten "uçuşta" olanlar) BİRLİKTE
            // devam eder — genuine concurrency, Task.Yield'in garanti
            // edemeyeceği kadar güvenilir bir biçimde.
            if (Interlocked.Increment(ref _arrivals) == _expectedChunks)
                _allArrived.TrySetResult();

            await _allArrived.Task;

            var schema = new DatabaseSchema { Name = "Fake" };
            foreach (var name in chunk.OwnedTables)
            {
                schema.Tables.Add(new SchemaTable
                {
                    Id = SchemaIdConvention.TableId(name),
                    Name = name,
                    Columns = new List<SchemaColumn>
                    {
                        new() { Id = SchemaIdConvention.ColumnId(name, "id"), Name = "id", Type = "uuid", IsPK = true },
                    },
                });
            }
            return schema;
        }

        public Task<DatabaseSchema> RepairAsync(
            DatabaseSchema schema, IReadOnlyList<string> findings, DatabaseType engine, CancellationToken ct = default) =>
            Task.FromResult(schema);

        public Task<int> EffectiveMaxOutputTokensAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class ReportingFailedException : Exception
    {
        public ReportingFailedException(string message) : base(message) { }
    }

    /// <summary>
    /// Her <see cref="AgentStep"/> raporunu kaydeden ve içeride EŞ ZAMANLI
    /// girişi tespit eden bir geri çağırma. <see cref="AsyncProgress{T}"/>
    /// artık kendi çağrılarını serileştiriyor (bkz. AsyncProgress.cs'teki
    /// SemaphoreSlim) — bu callback'in gövdesine iki thread'in AYNI ANDA
    /// girmemesi gerekiyor, aksi hâlde <c>overlapDetected</c> true olur.
    /// </summary>
    private sealed class OverlapDetectingReporter
    {
        private int _insideCallback;
        private int _callCount;
        public volatile bool OverlapDetected;
        public readonly List<AgentStep> Reported = new();

        public AsyncProgress<AgentStep> AsProgress { get; }

        public OverlapDetectingReporter()
        {
            AsProgress = new AsyncProgress<AgentStep>(async step =>
            {
                if (Interlocked.Increment(ref _insideCallback) > 1)
                    OverlapDetected = true;

                try
                {
                    // Pencereyi bilerek genişletiyoruz: gerçek bir çakışma
                    // varsa bu bekleme sırasında ikinci bir thread içeri
                    // girip _insideCallback'i 2'ye çıkarabilsin.
                    await Task.Delay(20);

                    lock (Reported) Reported.Add(step);

                    var callIndex = Interlocked.Increment(ref _callCount);
                    if (callIndex == 2)
                        throw new ReportingFailedException(
                            "simulated SSE write failure on the second concurrent report");
                }
                finally
                {
                    Interlocked.Decrement(ref _insideCallback);
                }
            });
        }
    }

    [Fact]
    public async Task Two_chunks_genuinely_race_and_their_progress_reports_never_overlap()
    {
        var source = new GatedAsyncSchemaDraftSource(expectedChunks: 2);
        var reporter = new OverlapDetectingReporter();

        // Raporlayıcı ikinci çağrıda kasıtlı olarak fırlatıyor — bu yüzden
        // hat bu istisnayı yutmadan yukarı taşımalı (bkz. aşağıdaki test).
        await Assert.ThrowsAsync<ReportingFailedException>(() =>
            Pipeline(source).RunAsync(
                "bir mağaza şeması", DatabaseType.PostgreSQL, progress: reporter.AsProgress));

        // (a) — hiçbir zaman iki rapor aynı anda callback'in içinde olmadı,
        // İKİ PARÇA DA gerçekten uçuşta olmasına (GatedAsyncSchemaDraftSource)
        // rağmen.
        Assert.False(reporter.OverlapDetected,
            "AsyncProgress<T> serialize etmeliydi: iki chunk aynı anda callback'e girdi.");
    }

    [Fact]
    public async Task A_throwing_progress_reporter_is_not_reinterpreted_as_a_domain_generation_failure()
    {
        // Aynı senaryo: raporlayıcı patlıyor. Kritik iddia — hat bunu
        // "[merge] Domain 'X' failed: simulated SSE write failure" notuna
        // ÇEVİRMEMELİ (o zaman zaten üretilmiş, parası ödenmiş bir parça
        // yanlış sebeple kaybedilmiş olurdu). İstisna olduğu gibi yukarı
        // taşınmalı.
        var source = new GatedAsyncSchemaDraftSource(expectedChunks: 2);
        var reporter = new OverlapDetectingReporter();

        var ex = await Record.ExceptionAsync(() =>
            Pipeline(source).RunAsync(
                "bir mağaza şeması", DatabaseType.PostgreSQL, progress: reporter.AsProgress));

        // Fırlayan istisnanın KENDİSİ olmalı — bir "[merge] Domain ... failed"
        // InvalidOperationException'ına (ya da başka bir yeniden yorumlamaya)
        // sarılmamış olmalı.
        Assert.IsType<ReportingFailedException>(ex);
        Assert.DoesNotContain("Domain", ex!.Message);
        Assert.DoesNotContain("failed", ex.Message);
    }
}
