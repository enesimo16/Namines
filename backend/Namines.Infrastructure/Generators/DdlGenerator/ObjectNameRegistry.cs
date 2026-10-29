using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Namines.Core.Enums;

namespace Namines.Infrastructure.Generators.DdlGenerator;

/// <summary>
/// Kısıt ve index adlarının TEK dağıtım noktası: motorun uzunluk sınırına
/// sığdırır ve çakışmaları giderir.
///
/// <b>Neden:</b> eski <c>Fit</c> yalnızca Oracle'da ve yalnızca kısıtlarda adı
/// 30 karaktere KESİYORDU — kesildikten sonra tekilleştirme yapılmadığı için iki
/// uzun ad aynı öneki paylaşınca yeniden çakışıyor, PostgreSQL (63) ve
/// MySQL/MariaDB (64) sınırları hiç uygulanmıyor, index adları hiç
/// tekilleştirilmiyor ve enum CHECK adları kayıt dışı kalıyordu. PostgreSQL
/// aşan adı sessizce keser; iki kesik ad çakışınca DDL "already exists" ile düşer.
///
/// <b>Kesme kuralı:</b> yalnızca sınırı AŞAN ad değişir; kalan ön ek +
/// <c>_</c> + tam adın SHA-256'sından 8 hex. Hash tam addan türediği için
/// deterministik ve aynı öneki paylaşan iki uzun ad ayrışır. Sınıra sığan ve
/// çakışmayan ad BİREBİR korunur (golden dosyalar bu yüzden değişmez).
///
/// <b>Ad uzayları motora göre farklıdır</b> ve bilerek en dar geçerli kapsam
/// seçildi — gereksiz yere yeniden adlandırma çıktıyı kararsızlaştırırdı:
/// <list type="bullet">
/// <item>Kısıtlar (PK/UNIQUE/CHECK/FK): PostgreSQL'de tablo başına, diğerlerinde şema genelinde.</item>
/// <item>Index'ler: MySQL/MariaDB/MSSQL'de tablo başına, PostgreSQL/Oracle/SQLite'ta şema genelinde.</item>
/// <item>PK ve UNIQUE kısıtı aynı adlı bir index de yarattığı için iki uzaya birden yazılır.</item>
/// </list>
/// </summary>
internal sealed class ObjectNameRegistry
{
    private const int HashLength = 8;

    private readonly DatabaseType _engine;
    private readonly HashSet<string> _constraintsGlobal = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _indexesGlobal = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _constraintsByTable = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _indexesByTable = new(StringComparer.OrdinalIgnoreCase);

    public ObjectNameRegistry(DatabaseType engine) => _engine = engine;

    /// <summary>
    /// Motorun tanımlayıcı sınırı. Oracle için 30: üretici baştan beri 12c ve
    /// öncesini hedefliyor (12.2+ 128 izin verir ama eski sürümde DDL düşer);
    /// PostgreSQL/Oracle BAYT, MySQL/MariaDB/MSSQL karakter sayar.
    /// </summary>
    public static int MaxLength(DatabaseType engine) => engine switch
    {
        DatabaseType.Oracle => 30,
        DatabaseType.PostgreSQL => 63,
        DatabaseType.MySQL or DatabaseType.MariaDB => 64,
        DatabaseType.MSSQL => 128,
        _ => int.MaxValue, // SQLite sınır koymaz
    };

    /// <summary>
    /// Kısıt adı alır. <paramref name="createsIndex"/>: PK ve UNIQUE, aynı adlı
    /// bir index de yaratır, o yüzden index ad uzayına da yazılır.
    /// </summary>
    public string ClaimConstraint(string tableName, string desired, bool createsIndex = false)
    {
        var sets = new List<HashSet<string>> { ConstraintSet(tableName) };
        if (createsIndex) sets.Add(IndexSet(tableName));
        return Claim(desired, sets);
    }

    /// <summary>Index adı alır.</summary>
    public string ClaimIndex(string tableName, string desired) =>
        Claim(desired, [IndexSet(tableName)]);

    private HashSet<string> ConstraintSet(string tableName) =>
        _engine == DatabaseType.PostgreSQL ? PerTable(_constraintsByTable, tableName) : _constraintsGlobal;

    private HashSet<string> IndexSet(string tableName) =>
        _engine is DatabaseType.MySQL or DatabaseType.MariaDB or DatabaseType.MSSQL
            ? PerTable(_indexesByTable, tableName)
            : _indexesGlobal;

    private static HashSet<string> PerTable(Dictionary<string, HashSet<string>> map, string tableName)
    {
        if (!map.TryGetValue(tableName, out var set))
            map[tableName] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return set;
    }

    private string Claim(string desired, List<HashSet<string>> sets)
    {
        var candidate = Shorten(desired, _engine);
        var suffix = 2;

        // Sayaç ekli ad da sığdırılır: "_2" eklemek sınırı yeniden aşabilir.
        while (sets.Exists(s => s.Contains(candidate)))
            candidate = Shorten($"{desired}_{suffix++}", _engine);

        foreach (var set in sets) set.Add(candidate);
        return candidate;
    }

    /// <summary>
    /// Ad motor sınırını aşıyorsa kesip deterministik kısa hash ekler; aşmıyorsa aynen döner.
    /// </summary>
    internal static string Shorten(string name, DatabaseType engine)
    {
        var limit = MaxLength(engine);
        if (Measure(name, engine) <= limit) return name;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))
            .ToLowerInvariant()[..HashLength];
        var tail = "_" + hash;

        var sb = new StringBuilder();
        var used = Measure(tail, engine);
        // Rune rune ilerlenir: kesme, vekil çiftin ya da çok baytlı karakterin ortasına denk gelmesin.
        foreach (var rune in name.EnumerateRunes())
        {
            var piece = rune.ToString();
            var cost = Measure(piece, engine);
            if (used + cost > limit) break;
            sb.Append(piece);
            used += cost;
        }

        return sb + tail;
    }

    private static int Measure(string name, DatabaseType engine) =>
        engine is DatabaseType.PostgreSQL or DatabaseType.Oracle
            ? Encoding.UTF8.GetByteCount(name)
            : name.Length;
}
