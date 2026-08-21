using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Interfaces;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Infrastructure.Generators.Eject;

/// <summary>Hedef adından üreticiye. Bilinmeyen hedef sessizce boş çıktı DÖNDÜRMEZ.</summary>
public interface IEjectGeneratorRegistry
{
    IReadOnlyList<IEjectGenerator> All { get; }

    /// <summary>Bilinmeyen hedefte geçerli listeyi içeren bir hata fırlatır.</summary>
    IEjectGenerator Get(string target);
}

/// <summary>
/// new-phase/12-CODEGEN-EJECT.md'deki hedeflerin kaydı.
///
/// Kayıt defteri açıkça yazılıyor, yansımayla (reflection) taranmıyor: yansıma,
/// yanlışlıkla eklenen ya da yarım kalmış bir üreticiyi sessizce yayına sokar.
/// Bir hedefin listeye girmesi bilinçli bir karar olmalı.
/// </summary>
public sealed class EjectGeneratorRegistry : IEjectGeneratorRegistry
{
    private readonly Dictionary<string, IEjectGenerator> _byTarget;

    public EjectGeneratorRegistry(IDdlGeneratorFactory ddlFactory)
    {
        var generators = new IEjectGenerator[]
        {
            // Tipler ve sözleşmeler
            new TypeScriptTypesGenerator(),
            new ZodSchemaGenerator(),
            new CSharpTypesGenerator(),
            new PydanticGenerator(),
            new GraphqlSdlGenerator(),
            new JsonSchemaGenerator(),
            new ProtobufGenerator(),

            // ORM'ler
            new DrizzleGenerator(),
            new TypeOrmGenerator(),
            new SqlAlchemyGenerator(),
            new DjangoGenerator(),
            new SequelizeGenerator(),
            new GormGenerator(),

            // Konsol (07 §8)
            new ConsoleNextjsGenerator(),

            // Migration biçimleri — DDL üreticisini yeniden kullanırlar.
            new FlywayGenerator(ddlFactory),
            new LiquibaseGenerator(ddlFactory),
        };

        _byTarget = generators.ToDictionary(g => g.Target, StringComparer.OrdinalIgnoreCase);
        All = generators;
    }

    public IReadOnlyList<IEjectGenerator> All { get; }

    public IEjectGenerator Get(string target)
    {
        if (!string.IsNullOrWhiteSpace(target) && _byTarget.TryGetValue(target.Trim(), out var generator))
            return generator;

        // Geçerli listeyi mesaja koymak, çağıranın dokümana gitmeden düzeltmesini sağlar.
        throw new NotSupportedException(
            $"Unknown eject target '{target}'. Available: {string.Join(", ", _byTarget.Keys.OrderBy(k => k))}.");
    }
}
