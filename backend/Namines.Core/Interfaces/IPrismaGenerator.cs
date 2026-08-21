using System.Collections.Generic;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.Core.Interfaces;

/// <summary>
/// Şemadan Prisma <c>schema.prisma</c> üretir (new-phase/12-CODEGEN-EJECT.md).
///
/// <see cref="IEfCoreGenerator"/>'dan farklı olarak yalnızca dosya değil,
/// <b>uyarı</b> da döndürür. Sebebi ürünün temel ilkesi: Prisma'nın karşılığı
/// olmayan yapılar (CHECK kısıtları, kısmi index'ler, bazı motor özellikleri)
/// sessizce düşürülürse üretilen şema veritabanından DAHA GEVŞEK olur — ve
/// kullanıcı o dosyadan <c>prisma db push</c> çalıştırdığında kısıtı kaybeder.
/// Sessiz kayıp, en tehlikeli hata sınıfıdır; ne düşürüldüğü açıkça söylenir.
/// </summary>
public interface IPrismaGenerator
{
    PrismaGenerationResult Generate(DatabaseSchema schema, DatabaseType engine);
}

/// <param name="Files">Dosya adı → içerik.</param>
/// <param name="Warnings">
/// Prisma'ya çevrilemeyen ve bu yüzden çıktıda BULUNMAYAN yapılar.
/// Boş değilse üretilen şema veritabanının tam karşılığı değildir.
/// </param>
public sealed record PrismaGenerationResult(
    Dictionary<string, string> Files,
    List<string> Warnings);
