using System.Collections.Generic;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

/// <summary>
/// Şemadan bir hedef teknolojiye kod/şema üretir (new-phase/12-CODEGEN-EJECT.md).
///
/// Her hedef aynı sözleşmeyi paylaşır ve sözleşmenin en önemli parçası
/// <see cref="EjectResult.Warnings"/>: hedefin İFADE EDEMEDİĞİ her yapı burada
/// bildirilir. Prisma üreticisinde öğrenilen ders — CHECK kısıtını sessizce
/// düşüren bir çıktı, o dosyadan şema uygulandığında veritabanından kısıtı
/// düşürür. Üretilen dosyanın "çalışır görünmesi" yetmez; neyi taşımadığını
/// söylemesi gerekir.
/// </summary>
public interface IEjectGenerator
{
    /// <summary>İstemcinin seçtiği kimlik — <c>orm.drizzle</c>, <c>types.typescript</c>.</summary>
    string Target { get; }

    /// <summary>İnsan tarafından okunan ad.</summary>
    string DisplayName { get; }

    EjectResult Generate(DatabaseSchema schema, DatabaseType engine);
}

/// <param name="Files">Dosya adı → içerik.</param>
/// <param name="Warnings">
/// Hedefe çevrilemeyen ve bu yüzden çıktıda BULUNMAYAN yapılar. Boş değilse
/// üretilen dosya şemanın tam karşılığı değildir.
/// </param>
public sealed record EjectResult(Dictionary<string, string> Files, List<string> Warnings);
