using System.Collections.Generic;
using Namines.Core.Models;

namespace Namines.Core.Analysis;

/// <summary>
/// second-phase/11-KODDAN-SEMA.md — bir depodaki model/entity tanımlarından
/// çıkarılan şema ve <b>neyin okunamadığı.</b>
///
/// <b>Neden "atlananlar" ayrı bir alan:</b> doc'un açık kuralı — "12 modelin
/// 9'u okundu, 3'ü anlaşılamadı" dürüstçe raporlanmalı. Eksik olanı sessizce
/// atlamak, olmayan bir tam resim sunar ve kullanıcı bu resme dayanıp
/// "veritabanım kodla uyumlu" sonucuna varır.
/// </summary>
/// <param name="Schema">Okunabilen modellerden kurulan şema.</param>
/// <param name="Format">Tanınan format: "prisma" | "efcore".</param>
/// <param name="ParsedModels">Başarıyla okunan model/entity adları.</param>
/// <param name="Skipped">Atlanan her şey ve nedeni — kullanıcıya olduğu gibi gösterilir.</param>
public sealed record CodeExtractionResult(
    DatabaseSchema Schema,
    string Format,
    IReadOnlyList<string> ParsedModels,
    IReadOnlyList<SkippedItem> Skipped);

/// <param name="Name">Atlanan model/alan adı.</param>
/// <param name="Reason">Neden atlandığı — tahmin değil, ayrıştırıcının gerçekten karşılaştığı durum.</param>
public sealed record SkippedItem(string Name, string Reason);
