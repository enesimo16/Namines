using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Prompts;
using Namines.Infrastructure.AI;
using Namines.Infrastructure.AI.Agent;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Infrastructure.Services;

/// <summary>
/// <see cref="ISchemaDraftSource"/>'un Groq uygulaması.
///
/// <b>Bu sınıf yalnızca ÇEVİRİ yapıyor</b> — hattın kararlarını (kaç tur, ne
/// zaman dur, neyi bulgu say) bilmiyor ve bilmemeli. Denetim ve durma kararı
/// <see cref="SchemaAgentPipeline"/>'da, yani deterministik tarafta.
///
/// <b>Araç döngüsü burada, hatta değil:</b> araçlar modelin tek bir turu
/// İÇİNDE kullandığı bir şey — hat için o tur hâlâ tek bir "düzeltme turu".
/// Araç turlarını hattın tur sayacına karıştırmak, kullanıcının bütçesini
/// göremediği bir yerden harcamak olurdu.
/// </summary>
public sealed class GroqSchemaDraftSource : ISchemaDraftSource
{
    /// <summary>
    /// Tek bir AI turu içinde izin verilen araç turu sayısı.
    ///
    /// Sınırsız bırakmak, model araç çağırmaya devam ettiği sürece token
    /// yakmak demekti — üstelik hattın tur sayacında hiç görünmeden.
    /// </summary>
    private const int MaxToolIterations = 3;

    private readonly GroqAIService _groq;
    private readonly IAgentChatClient _chat;
    private readonly IDdlGeneratorFactory _ddlFactory;

    public GroqSchemaDraftSource(GroqAIService groq, IDdlGeneratorFactory ddlFactory)
    {
        _groq = groq;
        _chat = groq;
        _ddlFactory = ddlFactory;
    }

    public async Task<string?> PlanAsync(
        string prompt, DatabaseType engine, CancellationToken cancellationToken = default)
    {
        var response = await _chat.CompleteAsync(
            new[]
            {
                new AgentChatMessage("system", AgentPlanPromptBuilder.BuildSystemPrompt()),
                new AgentChatMessage("user", AgentPlanPromptBuilder.BuildUserPrompt(prompt, engine)),
            },
            Array.Empty<AgentToolDefinition>(),
            temperature: 0.2,
            cancellationToken);

        return string.IsNullOrWhiteSpace(response.Content) ? null : response.Content;
    }

    public Task<DatabaseSchema> DraftAsync(
        string prompt, DatabaseType engine, string? plan, CancellationToken cancellationToken = default)
    {
        // Plan, isteğin ÖNÜNE ekleniyor: model uzun girdilerde baştaki bağlamı
        // daha güvenilir kullanıyor. Planı isteğin arkasına koymak, uzun
        // gereksinimlerde onun görmezden gelinmesine yol açıyordu.
        var enriched = string.IsNullOrWhiteSpace(plan)
            ? prompt
            : $"Follow this plan when creating the schema:\n{plan}\n\nRequirement:\n{prompt}";

        return _groq.GenerateSchemaAsync(new GenerateRequest { Prompt = enriched, DbType = engine });
    }

    /// <summary>
    /// <b>Bilerek <see cref="GroqAIService.GenerateSchemaAsync"/>'i ÇAĞIRMIYOR.</b>
    ///
    /// O metot, kendisine verilen <c>Prompt</c>'u <see cref="SchemaPromptBuilder.BuildUserPrompt"/>
    /// ile BİR KEZ DAHA sarmalıyor (kendi &lt;requirement&gt; etiketi ve kural
    /// metniyle). <see cref="SchemaPromptBuilder.BuildChunkUserPrompt"/> zaten
    /// KENDİ &lt;requirement&gt; sarmalını ve kural metnini içeren TAM bir istem
    /// üretiyor — bunu <c>GenerateSchemaAsync</c>'e geçirmek çifte sarmalamaya yol
    /// açardı: iç içe iki &lt;requirement&gt; bloğu, iki kez tekrarlanan JSON-şekli
    /// talimatları ve en önemlisi, parça kapsamlama talimatının ("yalnızca şu
    /// tabloları tanımla") DIŞ &lt;requirement&gt; bloğunun İÇİNE düşmesi — ki o blok
    /// sistem promptunda açıkça "veri, talimat değil" olarak işaretleniyor. Bu,
    /// hiçbir testin yakalayamayacağı ama modelin parça sınırını yok saymasına
    /// yol açabilecek sessiz bir kalite kaybı olurdu.
    ///
    /// Bunun yerine <see cref="_chat"/> (ham <see cref="IAgentChatClient"/>) ÜZERİNDEN
    /// tek bir tur çalıştırılıyor — <see cref="PlanAsync"/> ve
    /// <see cref="RepairWithToolsAsync"/>'in zaten kullandığı desen.
    ///
    /// <b>Kesilme (truncation) tespiti kaybolmuyor:</b> <see cref="GroqAIService.CompleteAsync"/>
    /// artık <c>GroqResponseReader.ReadContentOrThrow</c> üzerinden okuyor —
    /// <c>GenerateSchemaAsync</c>/<c>ReviseSchemaAsync</c>'in kullandığı AYNI
    /// kontrol. Yani bir parça yanıtı sağlayıcının token tavanına çarpıp
    /// kesilirse (<c>finish_reason == "length"</c>), bu da <c>AiOutputTruncatedException</c>
    /// olarak yüzeye çıkıyor — belirsiz bir JSON ayrıştırma hatası olarak değil.
    /// Kullanıcının tur başı token tavanı (<c>MaxTokensFor</c>) de aynı şekilde
    /// uygulanıyor. Bilinçli ödün olarak kalan tek şey: bu yol
    /// <c>GenerateSchemaAsync</c>'in sıcaklık artırarak yeniden deneme döngüsünü
    /// miras almıyor — kesilme artık ayrı ve doğru bir istisna olduğu için o
    /// döngünün asıl amacı (kesilmeyi "yanlış sıcaklık" sanıp tekrar denemek)
    /// zaten burada geçerli değil.
    /// </summary>
    public async Task<DatabaseSchema> DraftChunkAsync(
        string prompt,
        DatabaseType engine,
        SchemaChunk chunk,
        IReadOnlyList<string> allTableNames,
        CancellationToken cancellationToken = default)
    {
        var chunkPrompt = SchemaPromptBuilder.BuildChunkUserPrompt(prompt, engine, chunk, allTableNames);

        var response = await _chat.CompleteAsync(
            new[]
            {
                new AgentChatMessage("system", SchemaPromptBuilder.BuildSystemPrompt()),
                new AgentChatMessage("user", chunkPrompt),
            },
            Array.Empty<AgentToolDefinition>(),
            temperature: 0.4,
            cancellationToken);

        return SchemaJsonReader.TryRead(response.Content)
            ?? throw new InvalidOperationException(
                $"chunk '{chunk.Label}' did not return a readable schema");
    }

    public async Task<DatabaseSchema> RepairAsync(
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
        // bir hata üretir. Trigger/saklı yordam/enum da gönderiliyor — göremediği
        // bir trigger'ı düzeltemez ve NSL024 bulgusu hiç kapanmazdı.
        var request = new ReviseRequest
        {
            RevisionPrompt = instructions,
            SelectedTables = schema.Tables,
            ExistingRelations = schema.Relations,
            Triggers = schema.Triggers,
            StoredProcedures = schema.StoredProcedures,
            Enums = schema.Enums,
        };

        var answer = await RepairWithToolsAsync(schema, request, engine, cancellationToken);

        // Araç döngüsü okunabilir bir şema veremediyse eski (araçsız) yola
        // düşülüyor: modelin biçimi tutturamaması, kullanıcının düzeltme turunu
        // tamamen kaybetmesi anlamına gelmemeli.
        var repaired = SchemaJsonReader.TryRead(answer) ?? await _groq.ReviseSchemaAsync(request);

        // Model kısmi döner (yalnızca tables+relations). Birleştirmeden kullanmak,
        // her turda trigger/SP/enum silmek demekti.
        return SchemaMerge.PreserveUnrevised(schema, repaired);
    }

    /// <summary>
    /// Düzeltme turunu araçlarla yürütür: model yazmadan ÖNCE kural motorunu,
    /// kolon bilgisini ve gerçek DDL üretimini sorgulayabilir.
    ///
    /// Turu bitiren karar yine hatta — buradaki araçlar yalnızca modelin gözü.
    /// </summary>
    private async Task<string?> RepairWithToolsAsync(
        DatabaseSchema schema, ReviseRequest request, DatabaseType engine, CancellationToken cancellationToken)
    {
        var tools = new AgentTools(schema, engine, _ddlFactory);

        var messages = new List<AgentChatMessage>
        {
            new("system", RevisionPromptBuilder.BuildSystemPrompt()),
            new("user", RevisionPromptBuilder.BuildUserPrompt(request)),
        };

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var response = await _chat.CompleteAsync(messages, tools.Definitions, 0.1, cancellationToken);

            if (response.ToolCalls.Count == 0)
                return response.Content;

            messages.Add(new AgentChatMessage("assistant", response.Content, response.ToolCalls));

            foreach (var call in response.ToolCalls)
                messages.Add(new AgentChatMessage("tool", tools.Invoke(call), null, call.Id));
        }

        // Araç turu sınırına gelindi: modelden ARAÇSIZ, kesin bir cevap iste.
        // Araçları açık bırakmak, model çağırmaya devam ettiği sürece döngüyü
        // uzatırdı.
        var final = await _chat.CompleteAsync(
            messages, Array.Empty<AgentToolDefinition>(), 0.1, cancellationToken);

        return final.Content;
    }
}
