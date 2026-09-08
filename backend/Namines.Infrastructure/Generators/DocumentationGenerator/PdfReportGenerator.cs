using System;
using System.Globalization;
using System.Linq;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Namines.Infrastructure.Generators.DocumentationGenerator;

public class PdfReportGenerator
{
    /// <summary>
    /// Kapak sayfası + AI yönetici özeti + tablo detaylarından oluşan kurumsal PDF üretir.
    /// </summary>
    /// <param name="language">
    /// Çağıran taraflarla uyum için korunuyor. Rapor artık tek dilde (İngilizce)
    /// üretiliyor — ürünün geri kalanıyla aynı dil.
    /// </param>
    public byte[] Generate(DatabaseSchema schema, string projectSummary, string language = "en")
    {
        var document = Document.Create(container =>
        {
            // ── Kapak Sayfası ───────────────────────────────────────────────
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                page.Content().Element(x => ComposeCoverPage(x, schema, projectSummary));
            });

            // ── İçerik Sayfaları ────────────────────────────────────────────
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                page.Header().Element(x => ComposeContentHeader(x));
                page.Content().Element(x => ComposeContent(x, schema));
                page.Footer().Element(x => ComposeFooter(x));
            });
        });

        return document.GeneratePdf();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // KAPAK SAYFASI
    // ─────────────────────────────────────────────────────────────────────────
    private void ComposeCoverPage(IContainer container, DatabaseSchema schema, string projectSummary)
    {
        container.Column(col =>
        {
            // Üst renkli bant
            col.Item()
               .Height(280)
               .Background("#1e1b4b") // indigo-900
               .Padding(40)
               .Column(header =>
               {
                   header.Spacing(8);

                   // Logo / marka metni
                   header.Item()
                         .Text("NAMINES")
                         .FontSize(36)
                         .Bold()
                         .FontColor("#a5b4fc");   // indigo-300

                   header.Item()
                         .Text("Database Architecture & Data Dictionary")
                         .FontSize(14)
                         .FontColor("#c7d2fe");   // indigo-200

                   // Tarih
                   string dateLabel = "Generation Date";
                   string dateFormat = "MMMM dd, yyyy HH:mm";
                   header.Item()
                         .PaddingTop(16)
                         .Text($"{dateLabel}: {DateTime.Now.ToString(dateFormat, CultureInfo.InvariantCulture)}")
                         .FontSize(10)
                         .FontColor("#6366f1");   // indigo-500
               });

            // Beyaz içerik alanı
            col.Item()
               .Padding(40)
               .Column(body =>
               {
                   body.Spacing(20);

                   // Proje adı
                   body.Item()
                       .Text(schema.Name ?? "Untitled Schema")
                       .FontSize(28)
                       .Bold()
                       .FontColor("#1e1b4b");

                   // İstatistik kartları satırı
                   body.Item()
                       .Row(row =>
                       {
                           StatCard(row.RelativeItem(), "Total Tables", schema.Tables.Count.ToString(), "#6366f1");
                           row.ConstantItem(12);
                           StatCard(row.RelativeItem(), "Total Relations", schema.Relations.Count.ToString(), "#8b5cf6");
                           row.ConstantItem(12);
                           StatCard(row.RelativeItem(), "Total Columns",
                               schema.Tables.Sum(t => t.Columns.Count).ToString(), "#06b6d4");
                       });

                   // Ayırıcı çizgi
                   body.Item()
                       .PaddingVertical(8)
                       .LineHorizontal(1)
                       .LineColor("#e0e7ff");

                   // Yönetici Özeti başlığı
                   body.Item()
                       .Text("EXECUTIVE SUMMARY")
                       .FontSize(10)
                       .Bold()
                       .FontColor("#6366f1")
                       .LetterSpacing(2);

                   // AI tarafından üretilen özet
                   if (!string.IsNullOrWhiteSpace(projectSummary))
                   {
                       body.Item()
                           .Text(projectSummary)
                           .FontSize(10.5f)
                           .FontColor("#374151")
                           .LineHeight(1.6f);
                   }
                   else
                   {
                       body.Item()
                           .Text("This report was generated automatically by Namines.")
                           .FontSize(10)
                           .FontColor("#6b7280")
                           .Italic();
                   }
               });

            // Alt footer bant
            col.Item()
               .Height(40)
               .Background("#f5f3ff")  // indigo-50
               .PaddingHorizontal(40)
               .AlignMiddle()
               .Text("Namines · AI-Powered Database Design Tool")
               .FontSize(8)
               .FontColor("#6366f1");
        });
    }

    private static void StatCard(IContainer container, string label, string value, string color)
    {
        container
            .Background("#f8fafc")
            .Border(1)
            .BorderColor("#e2e8f0")
            .CornerRadius(6)
            .Padding(12)
            .Column(c =>
            {
                c.Item().Text(value).FontSize(22).Bold().FontColor(color);
                c.Item().Text(label).FontSize(9).FontColor("#64748b");
            });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // İÇERİK SAYFASI HEADER
    // ─────────────────────────────────────────────────────────────────────────
    private void ComposeContentHeader(IContainer container)
    {
        container
            .BorderBottom(1)
            .BorderColor("#e0e7ff")
            .PaddingBottom(8)
            .Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("NAMINES").FontSize(16).Bold().FontColor("#4f46e5");
                    col.Item().Text("Data Dictionary").FontSize(9).FontColor("#94a3b8");
                });
                row.ConstantItem(120)
                   .AlignRight()
                   .AlignBottom()
                   .Text(DateTime.Now.ToString("dd MMM yyyy", CultureInfo.InvariantCulture))
                   .FontSize(9)
                   .FontColor("#94a3b8");
            });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // İÇERİK (Tablo Detayları)
    // ─────────────────────────────────────────────────────────────────────────
    private void ComposeContent(IContainer container, DatabaseSchema schema)
    {
        container.PaddingVertical(1, Unit.Centimetre).Column(column =>
        {
            column.Spacing(20);

            string schemaLabel = "Schema";
            column.Item()
                  .Text($"{schemaLabel}: {schema.Name ?? "Untitled Schema"}")
                  .FontSize(18)
                  .SemiBold()
                  .FontColor("#1e1b4b");

            string tablesCountLabel = "Total Tables";
            string relationsCountLabel = "Total Relations";
            column.Item()
                  .Row(row =>
                  {
                      row.RelativeItem().Text($"{tablesCountLabel}: {schema.Tables.Count}").FontSize(11);
                      row.RelativeItem().Text($"{relationsCountLabel}: {schema.Relations.Count}").FontSize(11);
                  });

            string tableLabel = "Table";
            foreach (var table in schema.Tables)
            {
                column.Item()
                      .PaddingTop(10)
                      .Row(row =>
                      {
                          row.RelativeItem()
                             .BorderLeft(3)
                             .BorderColor("#6366f1")
                             .PaddingLeft(8)
                             .Text($"{tableLabel}: {table.Name}")
                             .FontSize(14)
                             .Bold()
                             .FontColor("#3730a3");
                      });

                column.Item().Table(tableContainer =>
                {
                    tableContainer.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(130);
                        columns.ConstantColumn(110);
                        columns.ConstantColumn(55);
                        columns.RelativeColumn();
                    });

                    tableContainer.Header(header =>
                    {
                        header.Cell().Background("#ede9fe").PaddingVertical(5).PaddingHorizontal(4)
                              .Text("Column Name").SemiBold().FontSize(9).FontColor("#4338ca");
                        header.Cell().Background("#ede9fe").PaddingVertical(5).PaddingHorizontal(4)
                              .Text("Data Type").SemiBold().FontSize(9).FontColor("#4338ca");
                        header.Cell().Background("#ede9fe").PaddingVertical(5).PaddingHorizontal(4)
                              .Text("Nullable?").SemiBold().FontSize(9).FontColor("#4338ca");
                        header.Cell().Background("#ede9fe").PaddingVertical(5).PaddingHorizontal(4)
                              .Text("Constraints (PK/FK)").SemiBold().FontSize(9).FontColor("#4338ca");
                    });

                    bool isOdd = false;
                    foreach (var col in table.Columns)
                    {
                        var desc = col.IsPK ? "Primary Key" : (col.IsFK ? "Foreign Key" : "");
                        var lengthStr = col.Length.HasValue ? $"({col.Length})" : "";
                        var nullStr = col.IsNullable ? "Yes" : "No";
                        var rowBg = isOdd ? "#fafaf9" : "#ffffff";
                        isOdd = !isOdd;

                        var descColor = col.IsPK ? "#d97706" : "#6366f1";

                        tableContainer.Cell().Background(rowBg).PaddingVertical(3).PaddingHorizontal(4).Text(col.Name).FontSize(9);
                        tableContainer.Cell().Background(rowBg).PaddingVertical(3).PaddingHorizontal(4).Text($"{col.Type}{lengthStr}").FontSize(9);
                        tableContainer.Cell().Background(rowBg).PaddingVertical(3).PaddingHorizontal(4).Text(nullStr).FontSize(9);
                        tableContainer.Cell().Background(rowBg).PaddingVertical(3).PaddingHorizontal(4).Text(desc).FontSize(9).FontColor(descColor);
                    }
                });
            }
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FOOTER
    // ─────────────────────────────────────────────────────────────────────────
    private void ComposeFooter(IContainer container)
    {
        container
            .BorderTop(1)
            .BorderColor("#e0e7ff")
            .PaddingTop(6)
            .Row(row =>
            {
                row.RelativeItem()
                   .Text("Namines — Automatically Generated")
                   .FontSize(8)
                   .FontColor("#94a3b8");

                row.ConstantItem(80).AlignRight().Text(x =>
                {
                    x.Span("Page ").FontSize(8).FontColor("#94a3b8");
                    x.CurrentPageNumber().FontSize(8).FontColor("#6366f1");
                    x.Span(" / ").FontSize(8).FontColor("#94a3b8");
                    x.TotalPages().FontSize(8).FontColor("#6366f1");
                });
            });
    }
}
