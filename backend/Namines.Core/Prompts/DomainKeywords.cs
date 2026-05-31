using System;
using System.Collections.Generic;
using System.Linq;
using Namines.Core.Models;

namespace Namines.Core.Prompts;

public static class DomainKeywords
{
    private static readonly Dictionary<string, string[]> KeywordsMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Lojistik & Ulaşım", new[] { "sefer", "rota", "bilet", "plaka", "arac", "sofor", "koltuk", "kalkis", "varis", "durak", "tasima", "shuttle", "trip", "ticket", "driver" } },
        { "Sağlık & Medikal", new[] { "hasta", "doktor", "hemsire", "recete", "ilac", "poliklinik", "randevu", "tahlil", "klinik", "hospital", "patient", "doctor", "appointment" } },
        { "E-Ticaret & Satış", new[] { "urun", "siparis", "sepet", "fatura", "stok", "kargo", "odeme", "indirim", "kupon", "musteri", "product", "order", "cart", "invoice", "stock", "customer" } },
        { "Eğitim & Okul", new[] { "ogrenci", "ogretmen", "ders", "sinav", "not", "akademisyen", "fakulte", "bolum", "kayit", "vize", "final", "student", "teacher", "course", "exam", "grade" } },
        { "Finans & Bankacılık", new[] { "hesap", "transfer", "bakiye", "iban", "kredi", "islem", "para", "kur", "cuzdan", "doviz", "account", "balance", "transaction", "credit" } }
    };

    public static string DetectDomain(DatabaseSchema schema)
    {
        if (schema?.Tables == null || schema.Tables.Count == 0)
            return "Genel Amaçlı";

        var domainScores = new Dictionary<string, int>();
        foreach (var domain in KeywordsMap.Keys)
        {
            domainScores[domain] = 0;
        }

        foreach (var table in schema.Tables)
        {
            IncrementScores(table.Name, domainScores);

            foreach (var col in table.Columns)
            {
                IncrementScores(col.Name, domainScores);
            }
        }

        var bestDomain = domainScores.OrderByDescending(x => x.Value).FirstOrDefault();
        return bestDomain.Value > 0 ? bestDomain.Key : "Genel Amaçlı";
    }

    private static void IncrementScores(string name, Dictionary<string, int> scores)
    {
        foreach (var kvp in KeywordsMap)
        {
            var domain = kvp.Key;
            var keywords = kvp.Value;
            foreach (var keyword in keywords)
            {
                if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    scores[domain] += 5;
                }
            }
        }
    }
}
