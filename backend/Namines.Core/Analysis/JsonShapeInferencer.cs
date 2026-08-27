using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Namines.Core.Analysis;

/// <param name="Name">Alan adı.</param>
/// <param name="Type">Kanonik tip tahmini.</param>
/// <param name="SeenCount">Bu alanın kaç örnekte görüldüğü.</param>
/// <param name="IsUncertain">
/// Yalnızca tek bir örnekte görüldü — doc'un kuralı: "tek yanıtta görülen bir
/// alan belirsiz işaretlenir". Opsiyonel bir alan mı yoksa tesadüf mü,
/// ayırt edilemez.
/// </param>
public sealed record InferredField(string Name, string Type, int SeenCount, bool IsUncertain);

/// <param name="Name">Varlık adı tahmini — uç nokta yolundan türetilir.</param>
/// <param name="Fields">Alanlar.</param>
/// <param name="SampleCount">Bu şeklin kaç kez görüldüğü.</param>
/// <param name="EndpointCount">Kaç FARKLI uç noktada görüldüğü.</param>
/// <param name="Confidence">
/// "high" | "medium" | "low". Doc'un kuralı: bir şekil kaç farklı uç noktada,
/// kaç kez görüldüyse güveni o kadar yüksek.
/// </param>
public sealed record InferredEntity(
    string Name,
    IReadOnlyList<InferredField> Fields,
    int SampleCount,
    int EndpointCount,
    string Confidence);

/// <param name="FromEntity">FK'yı taşıyan varlık.</param>
/// <param name="FromField">FK alanı, ör. <c>user_id</c>.</param>
/// <param name="ToEntity">İşaret ettiği varlık.</param>
public sealed record InferredRelation(string FromEntity, string FromField, string ToEntity);

public sealed record ShapeInferenceResult(
    IReadOnlyList<InferredEntity> Entities,
    IReadOnlyList<InferredRelation> Relations);

/// <param name="Endpoint">Yanıtın geldiği yol, ör. <c>/api/users</c>. Ad tahmini ve uç nokta sayımı için.</param>
/// <param name="Body">Ham JSON gövdesi.</param>
public sealed record ObservedResponse(string Endpoint, string Body);

/// <summary>
/// second-phase/06-VERI-KAYNAKLARI.md kademe 3 — gözlemlenen JSON
/// yanıtlarının ŞEKLİNDEN veri modeli çıkarımı.
///
/// <b>Değerler hiçbir aşamada saklanmaz.</b> Doc'un açık gizlilik kuralı:
/// yalnızca alan adı ve tip tutulur. Gerçek e-posta/isim/fiyat bu sınıftan
/// asla çıkmaz — <see cref="ShapeOf"/> yalnızca ad+tip üretir ve gövde
/// bundan sonra atılır.
///
/// <b>Extension'dan BAĞIMSIZ.</b> Kademe 3'ün asıl içeriği bu çıkarımdır;
/// tarayıcı extension'ı yalnızca taşıyıcıdır. Burada saf ve test edilebilir
/// tutmak, extension yazıldığında onun yalnızca <see cref="ObservedResponse"/>
/// göndermesini yeterli kılıyor — ve extension olmadan da (kullanıcı örnek
/// JSON yapıştırarak) çalışıyor.
///
/// <b>Çıkan her şey bir TAHMİN.</b> Doc'un kuralı: sonuç bir taslaktır,
/// kullanıcı kabul eder/yeniden adlandırır/reddeder. Bu sınıf hiçbir şeyi
/// otomatik onaylamaz; güven puanı ve "belirsiz" işaretleri tam da bu kararı
/// verilebilsin diye var.
/// </summary>
public static class JsonShapeInferencer
{
    /// <summary>Bir şeklin "varlık" sayılması için gereken en az alan sayısı — tek alanlı bir sarmalayıcı varlık değildir.</summary>
    private const int MinFieldsForEntity = 2;

    public static ShapeInferenceResult Infer(IReadOnlyList<ObservedResponse> responses)
    {
        // Şekil imzası → (alan adı → tip), örnek sayısı, uç noktalar, ad adayları
        var clusters = new Dictionary<string, Cluster>(StringComparer.Ordinal);

        foreach (var response in responses)
        {
            foreach (var (shape, nameHint) in ExtractShapes(response))
            {
                if (shape.Count < MinFieldsForEntity) continue;

                var signature = string.Join("|", shape.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}:{f.Value}"));

                if (!clusters.TryGetValue(signature, out var cluster))
                {
                    cluster = new Cluster(shape);
                    clusters[signature] = cluster;
                }

                cluster.SampleCount++;
                cluster.Endpoints.Add(response.Endpoint);
                if (!string.IsNullOrEmpty(nameHint)) cluster.NameHints.Add(nameHint);

                foreach (var field in shape)
                    cluster.FieldSeen[field.Key] = cluster.FieldSeen.GetValueOrDefault(field.Key) + 1;
            }
        }

        var entities = clusters.Values
            .Select(ToEntity)
            .OrderByDescending(e => e.SampleCount)
            .ToList();

        // Aynı ada düşen iki farklı şekil olabilir (ör. liste ve detay uçları
        // farklı alanlar döndürür). Adı tekilleştir — aksi hâlde ilişki
        // çözümlemesi hangisine bağlanacağını bilemez.
        entities = Deduplicate(entities);

        return new ShapeInferenceResult(entities, InferRelations(entities));
    }

    private sealed class Cluster
    {
        public Cluster(Dictionary<string, string> shape) => Shape = shape;
        public Dictionary<string, string> Shape { get; }
        public int SampleCount;
        public HashSet<string> Endpoints { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> NameHints { get; } = new();
        public Dictionary<string, int> FieldSeen { get; } = new(StringComparer.Ordinal);
    }

    private static InferredEntity ToEntity(Cluster c)
    {
        var name = c.NameHints
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "unknown";

        var fields = c.Shape
            .Select(f => new InferredField(
                f.Key,
                f.Value,
                c.FieldSeen.GetValueOrDefault(f.Key),
                // Tek örnekte görülen alan belirsiz — ama şeklin KENDİSİ tek
                // kez görüldüyse hepsi aynı durumda demektir ve o zaman
                // "belirsiz" etiketi bilgi taşımaz; güven puanı zaten düşük.
                c.SampleCount > 1 && c.FieldSeen.GetValueOrDefault(f.Key) <= 1))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        return new InferredEntity(name, fields, c.SampleCount, c.Endpoints.Count, Score(c.SampleCount, c.Endpoints.Count));
    }

    /// <summary>Doc: "kaç farklı uç noktada, kaç kez görüldüyse güveni o kadar yüksek".</summary>
    private static string Score(int samples, int endpoints) =>
        endpoints >= 2 && samples >= 3 ? "high"
        : samples >= 2 ? "medium"
        : "low";

    private static List<InferredEntity> Deduplicate(List<InferredEntity> entities)
    {
        var result = new List<InferredEntity>();
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in entities)
        {
            if (!used.TryGetValue(e.Name, out var n))
            {
                used[e.Name] = 1;
                result.Add(e);
                continue;
            }

            used[e.Name] = n + 1;
            result.Add(e with { Name = $"{e.Name}_{n + 1}" });
        }

        return result;
    }

    /// <summary>
    /// Doc kademe 3 adım 3: <c>xxx_id</c> / <c>xxxId</c> deseni taşıyan bir
    /// alan, adı eşleşen BAŞKA bir varlığa işaret ediyorsa yabancı anahtar
    /// adayıdır.
    ///
    /// <b>Eşleşme yoksa ilişki UYDURULMAZ</b> — "external_ref_id" gibi bir
    /// alan yalnızca ada bakılarak bağlanırsa, olmayan bir ilişki üretilmiş olur.
    /// </summary>
    private static List<InferredRelation> InferRelations(IReadOnlyList<InferredEntity> entities)
    {
        var relations = new List<InferredRelation>();
        var byName = entities.ToDictionary(e => Singularise(e.Name), e => e, StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            foreach (var field in entity.Fields)
            {
                var target = TargetNameFromForeignKeyField(field.Name);
                if (target is null) continue;

                if (!byName.TryGetValue(target, out var targetEntity)) continue;
                if (ReferenceEquals(targetEntity, entity)) continue; // kendine bağ değil

                relations.Add(new InferredRelation(entity.Name, field.Name, targetEntity.Name));
            }
        }

        return relations;
    }

    /// <summary><c>user_id</c> → <c>user</c>, <c>authorId</c> → <c>author</c>. Değilse null.</summary>
    private static string? TargetNameFromForeignKeyField(string fieldName)
    {
        if (fieldName.Equals("id", StringComparison.OrdinalIgnoreCase)) return null;

        if (fieldName.EndsWith("_id", StringComparison.OrdinalIgnoreCase))
            return fieldName[..^3];

        if (fieldName.Length > 2 && fieldName.EndsWith("Id", StringComparison.Ordinal))
            return fieldName[..^2];

        return null;
    }

    /// <summary>Uç nokta yolları çoğul olur (<c>/api/users</c>), FK alanları tekil (<c>user_id</c>).</summary>
    private static string Singularise(string name) =>
        name.EndsWith("ies", StringComparison.OrdinalIgnoreCase) ? name[..^3] + "y"
        : name.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("ss", StringComparison.OrdinalIgnoreCase) ? name[..^1]
        : name;

    /// <summary>
    /// Bir yanıttan çıkarılabilecek tüm nesne şekilleri.
    ///
    /// Hem tek nesne (<c>{...}</c>), hem dizi (<c>[{...}]</c>), hem de yaygın
    /// sarmalayıcılar (<c>{"data": [...]}</c>) ele alınıyor — API'lerin çoğu
    /// gövdeyi sarmalar ve sarmalayıcıyı varlık sanmak, her uç nokta için
    /// sahte bir "data" varlığı üretirdi.
    /// </summary>
    private static IEnumerable<(Dictionary<string, string> Shape, string NameHint)> ExtractShapes(ObservedResponse response)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(response.Body).RootElement; }
        catch { yield break; } // bozuk/JSON olmayan gövde — sessizce atlanır, çıkarım bir "en iyi çaba"

        var hint = NameHintFromEndpoint(response.Endpoint);

        foreach (var element in Unwrap(root))
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            var shape = ShapeOf(element);
            if (shape.Count > 0) yield return (shape, hint);
        }
    }

    private static IEnumerable<JsonElement> Unwrap(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            // Diziden yalnızca ilk birkaç öğe — hepsi aynı şekle sahip olur ve
            // 1000 öğelik bir listeyi taramak aynı bilgiyi 1000 kez üretirdi.
            foreach (var item in root.EnumerateArray().Take(3)) yield return item;
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object) yield break;

        // Yaygın sarmalayıcılar: {"data": ...}, {"results": ...}, {"items": ...}
        foreach (var wrapper in new[] { "data", "results", "items", "records" })
        {
            if (!root.TryGetProperty(wrapper, out var inner)) continue;

            if (inner.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in inner.EnumerateArray().Take(3)) yield return item;
                yield break;
            }
            if (inner.ValueKind == JsonValueKind.Object)
            {
                yield return inner;
                yield break;
            }
        }

        yield return root;
    }

    /// <summary>
    /// Bir nesnenin şekli: alan adı → tip. <b>Değer ASLA okunmaz/saklanmaz</b>
    /// — yalnızca <see cref="JsonElement.ValueKind"/> ve sayılarda tam/ondalık
    /// ayrımı kullanılıyor.
    /// </summary>
    private static Dictionary<string, string> ShapeOf(JsonElement obj)
    {
        var shape = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var prop in obj.EnumerateObject())
        {
            var type = prop.Value.ValueKind switch
            {
                JsonValueKind.String => "VARCHAR",
                JsonValueKind.Number => prop.Value.TryGetInt64(out _) ? "BIGINT" : "DECIMAL",
                JsonValueKind.True or JsonValueKind.False => "BOOLEAN",
                // İç içe nesne/dizi ayrı bir varlık ya da JSON kolonu olabilir;
                // bu kademede kolon olarak JSON sayılıyor.
                JsonValueKind.Object or JsonValueKind.Array => "JSON",
                // null bir TİP değildir — hangi tip olduğu bilinemez, bu yüzden
                // uydurmak yerine bilinmez işaretleniyor.
                JsonValueKind.Null => "UNKNOWN",
                _ => "UNKNOWN",
            };

            shape[prop.Name] = type;
        }

        return shape;
    }

    /// <summary><c>/api/v1/users/42</c> → <c>users</c>. Sayısal ve sürüm parçaları atlanır.</summary>
    private static string NameHintFromEndpoint(string endpoint)
    {
        var path = endpoint.Split('?')[0];
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !long.TryParse(s, out _))                                  // /42
            .Where(s => !Guid.TryParse(s, out _))                                  // /a1b2...
            .Where(s => !s.Equals("api", StringComparison.OrdinalIgnoreCase))
            .Where(s => !System.Text.RegularExpressions.Regex.IsMatch(s, @"^v\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .ToList();

        return segments.Count > 0 ? segments[^1] : "unknown";
    }
}
