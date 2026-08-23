using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.AI;

namespace Namines.Infrastructure.Services;

/// <summary>
/// <see cref="ISchemaDraftSource"/>'un Groq uygulaması.
///
/// <b>Bu sınıf yalnızca ÇEVİRİ yapıyor</b> — hattın kararlarını (kaç tur, ne
/// zaman dur, neyi bulgu say) bilmiyor ve bilmemeli. Denetim ve durma kararı
/// <see cref="SchemaAgentPipeline"/>'da, yani deterministik tarafta.
/// </summary>
public sealed class GroqSchemaDraftSource : ISchemaDraftSource
{
    private readonly GroqAIService _groq;

    public GroqSchemaDraftSource(GroqAIService groq) => _groq = groq;

    public Task<DatabaseSchema> DraftAsync(string prompt, DatabaseType engine, CancellationToken cancellationToken = default) =>
        _groq.GenerateSchemaAsync(new GenerateRequest { Prompt = prompt, DbType = engine });

    public Task<DatabaseSchema> RepairAsync(
        DatabaseSchema schema,
        IReadOnlyList<string> findings,
        DatabaseType engine,
        CancellationToken cancellationToken = default)
    {
        // Bulgular MADDE MADDE veriliyor ve hiçbiri yorumlanmıyor: metinleri
        // linter ve DDL üreticilerinden geldiği gibi geçiyor. Özetlemek ya da
        // yeniden yazmak, modelin hangi kolonun hangi kuralı ihlal ettiğini
        // kaybetmesine yol açar — düzeltmesi istenen şeyin ta kendisi bu.
        var instructions =
            "The schema below was generated for " + engine + " and did not pass validation.\n" +
            "Fix ONLY these problems and return the corrected schema:\n\n" +
            string.Join("\n", findings.Select(f => "- " + f)) + "\n\n" +
            // "Başka bir şeye dokunma" demek şart: model her turda şemayı yeniden
            // tasarlarsa kullanıcının kabul ettiği tablolar tur tur değişir ve
            // düzeltme döngüsü hiçbir zaman yakınsamaz.
            "Keep every other table, column and relation exactly as it is. " +
            "Do not rename anything that is not named in the list above.";

        // TÜM tablolar gönderiliyor, yalnızca bulguya konu olanlar değil: model
        // göremediği bir tabloya yabancı anahtar yazdığında düzeltme turu yeni
        // bir hata üretir. ReviseSchemaAsync yalnızca gördüğü tabloları döndürür,
        // dolayısıyla eksik göndermek şemayı budamak demek olurdu.
        return _groq.ReviseSchemaAsync(new ReviseRequest
        {
            RevisionPrompt = instructions,
            SelectedTables = schema.Tables,
            ExistingRelations = schema.Relations,
        });
    }
}
