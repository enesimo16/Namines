using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
/// Bu özelliğin VAAT ETTİĞİ SAYIYI kilitler: "kapsamlı bir sistem istendiğinde
/// 50-60 tablo üretilebilmeli".
///
/// <b>Neden ayrı bir test dosyası:</b> bu sayının doğru olduğu bugüne kadar
/// yalnızca bir incelemede ARİTMETİKLE savunuldu (tablo başına ~600 token,
/// katman tavanları, parça hedefi) — hiçbir yerde ÇALIŞTIRILARAK
/// doğrulanmadı. Aritmetik, sabitlerden biri değiştiği gün sessizce yanlış
/// olur ve bunu kimse fark etmez. Buradaki testler o iddiayı çalışan bir
/// iddiaya çeviriyor: 54 tablo içeri, 54 tablo dışarı.
///
/// Gerçek AI YOK — <see cref="FakeSchemaDraftSource"/> her parçanın istediği
/// tabloları birebir üretiyor. Yani bu test modelin yeteneğini değil, HATTIN
/// büyük bir planı bölüp eksiksiz geri birleştirdiğini ölçüyor. Modelin
/// gerçekten 54 tablo yazıp yazmadığı ancak canlı bir uçla doğrulanabilir.
/// </summary>
public class SchemaAgentPipelineHeadlineScaleTests
{
    private static SchemaAgentPipeline Pipeline(ISchemaDraftSource source) =>
        new(source, new DdlGeneratorFactory(), NullLogger<SchemaAgentPipeline>.Instance);

    /// <summary>
    /// Kurumsal ölçekte gerçekçi bir plan: 9 alan, 54 tablo — kullanıcının
    /// "kapsamlı proje" derken kastettiği büyüklük.
    /// </summary>
    private static string ComprehensivePlanJson(out string[] allTables)
    {
        var domains = new (string Domain, string[] Tables)[]
        {
            ("Identity",   new[] { "users", "roles", "permissions", "user_roles", "sessions", "api_keys" }),
            ("Catalog",    new[] { "products", "categories", "brands", "variants", "product_categories", "product_images" }),
            ("Inventory",  new[] { "warehouses", "stock_levels", "stock_movements", "suppliers", "purchase_orders", "purchase_order_items" }),
            ("Ordering",   new[] { "orders", "order_items", "carts", "cart_items", "coupons", "order_coupons" }),
            ("Payments",   new[] { "payments", "refunds", "payment_methods", "invoices", "invoice_lines", "tax_rates" }),
            ("Shipping",   new[] { "shipments", "shipment_items", "carriers", "delivery_zones", "addresses", "tracking_events" }),
            ("Support",    new[] { "tickets", "ticket_messages", "ticket_tags", "faq_entries", "escalations", "agents" }),
            ("Analytics",  new[] { "page_views", "conversion_events", "funnels", "cohorts", "reports", "report_schedules" }),
            ("Governance", new[] { "audit_log", "data_retention_rules", "consents", "feature_flags", "settings", "webhooks" }),
        };

        allTables = domains.SelectMany(d => d.Tables).ToArray();

        var json = new StringBuilder("""{"schemaName":"commerce","domains":[""");
        for (var i = 0; i < domains.Length; i++)
        {
            if (i > 0) json.Append(',');
            var tables = string.Join(",", domains[i].Tables.Select(t => $"\"{t}\""));
            json.Append($$"""{"name":"{{domains[i].Domain}}","tables":[{{tables}}]}""");
        }
        json.Append("]}");
        return json.ToString();
    }

    [Fact]
    public async Task Kapsamli_bir_istek_54_tablonun_TAMAMINI_uretiyor()
    {
        var planJson = ComprehensivePlanJson(out var allTables);
        Assert.Equal(54, allTables.Length);   // fixture'ın kendisi doğru mu

        var source = new FakeSchemaDraftSource { PlanResponse = planJson };

        var result = await Pipeline(source).RunAsync(
            "kapsamli bir e-ticaret sistemi", DatabaseType.PostgreSQL, budgetRounds: 6);

        // ASIL İDDİA: hiçbir tablo yolda kaybolmuyor.
        Assert.Equal(54, result.Schema.Tables.Count);
        Assert.Equal(
            allTables.OrderBy(x => x, StringComparer.Ordinal),
            result.Schema.Tables.Select(t => t.Name).OrderBy(x => x, StringComparer.Ordinal));

        // Tek çağrıda ÜRETİLMEDİ — sağlayıcı tavanına sığmayacağı için bölündü.
        Assert.True(source.ChunkCalls.Count > 1,
            $"54 tablo tek parçada uretilemez; parca sayisi: {source.ChunkCalls.Count}");
        Assert.Equal(0, source.DraftCalls);   // tek-çağrı yoluna hiç düşülmedi

        // Ve birleşme temiz: kayıp/çakışma notu yok.
        Assert.Empty(result.MergeNotes);
    }

    [Fact]
    public async Task Hicbir_parca_saglayici_tavanini_asacak_kadar_buyuk_degil()
    {
        // Parça başına tablo sayısı, o parçanın çıktı token maliyetini belirliyor.
        // ~600 token/tablo ölçümüyle 9 tablo ≈ 5.400 token — en dar katmanın
        // (Free, 6.000) bile altında. Bu sınır kayarsa parçalar sessizce
        // kesilmeye başlar, ki bu özelliğin düzeltmek için var olduğu hatadır.
        var planJson = ComprehensivePlanJson(out _);
        var source = new FakeSchemaDraftSource { PlanResponse = planJson };

        await Pipeline(source).RunAsync(
            "kapsamli bir e-ticaret sistemi", DatabaseType.PostgreSQL, budgetRounds: 6);

        Assert.All(source.ChunkCalls, chunk =>
            Assert.True(
                chunk.OwnedTables.Count <= SchemaScopePartitioner.TargetTablesPerChunk,
                $"'{chunk.Label}' parcasi {chunk.OwnedTables.Count} tablo tasiyor, " +
                $"hedef {SchemaScopePartitioner.TargetTablesPerChunk}"));
    }

    [Fact]
    public async Task Her_parca_diger_TUM_tablolarin_adlarini_baglam_olarak_aliyor()
    {
        // Alanlar arası yabancı anahtarın tek dayanağı bu: bir parça, kendi
        // sahiplenmediği bir tabloya ilişki kuracaksa onun ADINI görmek zorunda.
        // Görmezse uydurur ve birleştirme o ilişkiyi düşürür.
        var planJson = ComprehensivePlanJson(out var allTables);
        var source = new FakeSchemaDraftSource { PlanResponse = planJson };

        await Pipeline(source).RunAsync(
            "kapsamli bir e-ticaret sistemi", DatabaseType.PostgreSQL, budgetRounds: 6);

        Assert.NotEmpty(source.ChunkContexts);
        Assert.All(source.ChunkContexts, context =>
            Assert.Equal(allTables.Length, context.Count));
    }
}
