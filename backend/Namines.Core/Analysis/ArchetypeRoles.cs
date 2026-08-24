using System.Collections.Generic;

namespace Namines.Core.Analysis;

/// <summary>
/// Her iş türü için modelin üstleneceği uzmanlık rolü ve o alanın
/// <b>somut</b> şema kuralları (36 §3).
///
/// <b>Neden ayrı roller:</b> "iyi bir şema tasarla" her alanda aynı şeyi
/// getiriyordu — kullanıcılar, bir ana tablo, birkaç yabancı anahtar. Oysa bir
/// fintech şemasında para hiçbir zaman kayan noktalı sayı olmamalı, bir IoT
/// şemasında ölçüm tablosu zaman damgasına göre bölümlenmeli, bir oyunda
/// envanter satır sayısı oyuncu sayısının katları olur. Bunlar o alanda çalışan
/// birinin bildiği, modelin ise sorulmadıkça getirmediği şeyler.
///
/// <b>Bu bir AI çağrısı DEĞİL.</b> Rolü seçmek için ikinci bir modele danışmak,
/// kullanıcı daha hiçbir şey görmeden bir tur harcamak olurdu; tür zaten
/// <see cref="ArchetypeDetector"/> tarafından anahtar kelimeden çıkarılıyor.
/// Rol yalnızca taslak prompt'una eklenen birkaç satır — maliyeti bir tur değil,
/// birkaç yüz token.
///
/// <b>Kurallar tavsiye, dayatma değil.</b> Şemayı yine deterministik kapı
/// (linter + gerçek DDL üreticisi) denetliyor; rol metni yalnızca modelin
/// başlangıç noktasını iyileştiriyor. Yanlış tür tespit edilirse maliyeti
/// düşük: birkaç alakasız tavsiye, hatalı bir şema değil.
/// </summary>
public static class ArchetypeRoles
{
    private static readonly Dictionary<ProjectArchetype, string> Briefs = new()
    {
        [ProjectArchetype.Ecommerce] =
            "You are a senior data architect who has shipped several online stores. " +
            "Keep the price a fixed-point decimal, never a float. Store the price paid ON the order line, " +
            "because a product's price changes and old orders must not change with it. " +
            "Give orders a status field rather than deleting them.",

        [ProjectArchetype.Marketplace] =
            "You are a data architect for multi-seller marketplaces. " +
            "Every listing belongs to a seller, and payouts are separate from payments: " +
            "the buyer pays once, the platform pays several sellers later. " +
            "Model commission as its own record so it can be audited.",

        [ProjectArchetype.Saas] =
            "You are a data architect for multi-tenant SaaS. " +
            "Every tenant-owned table carries the tenant id and it is part of the indexes, " +
            "because a query that forgets it leaks another customer's data. " +
            "Subscriptions have periods, not just a flag.",

        [ProjectArchetype.Erp] =
            "You are an ERP data architect. " +
            "Stock is a ledger of movements, not a single quantity column — a bare quantity cannot answer 'why'. " +
            "Documents (invoices, orders) are never edited in place: correct them with a new document. " +
            "Money is fixed-point decimal with an explicit currency.",

        [ProjectArchetype.Crm] =
            "You are a CRM data architect. " +
            "A contact and an account are different things and a contact can move between accounts. " +
            "Activities (calls, notes, emails) share one timeline table rather than one table per kind.",

        [ProjectArchetype.Game] =
            "You are a game backend data architect. " +
            "Inventory rows grow as players × items, so keep that table narrow and indexed by player. " +
            "Item definitions are separate from item instances. " +
            "Leaderboards read far more than they write — design for the read.",

        [ProjectArchetype.Social] =
            "You are a social platform data architect. " +
            "Follows are a directed edge table, not an array. " +
            "Counters (likes, followers) are derived: store the rows, do not trust a single counter column. " +
            "The feed query drives the indexes.",

        [ProjectArchetype.Cms] =
            "You are a content platform data architect. " +
            "Content has revisions and a publish state; overwriting a page loses the only copy of what was published. " +
            "Slugs are unique and stable. Taxonomy (tags, categories) is many-to-many.",

        [ProjectArchetype.Fintech] =
            "You are a financial systems data architect. " +
            "Money is NEVER a floating point type — use fixed-point decimal with an explicit currency. " +
            "Balances are derived from an append-only ledger of entries; rows are never updated or deleted. " +
            "Every entry carries an idempotency key so a retried request cannot double-charge.",

        [ProjectArchetype.Healthcare] =
            "You are a clinical systems data architect. " +
            "Records are appended and superseded, never deleted — history is part of the record. " +
            "Separate the person from their encounters. " +
            "Keep identifying data in as few tables as possible so access can be restricted.",

        [ProjectArchetype.Education] =
            "You are an education platform data architect. " +
            "A course and a course offering (a term, an instructor, a roster) are different things. " +
            "Enrolment carries its own state and dates. Grades are per assessment, not a single column.",

        [ProjectArchetype.Logistics] =
            "You are a logistics data architect. " +
            "A shipment moves through scanned events; the current status is derived from the last event. " +
            "Addresses are captured as they were at the time of shipping, not referenced live.",

        [ProjectArchetype.Iot] =
            "You are a telemetry data architect. " +
            "Readings are the biggest table by far: keep the row narrow, key it by device and time, " +
            "and never put text descriptions in it. Device metadata lives in its own small table.",

        [ProjectArchetype.Booking] =
            "You are a reservation systems data architect. " +
            "Availability and reservation are separate; the reservation must not be able to double-book, " +
            "so the uniqueness constraint covers resource plus time range. " +
            "Cancellation is a state, not a delete.",
    };

    /// <summary>
    /// Türün rol metni. Tanınmayan tür için <b>boş dizi</b> döner.
    ///
    /// Genel bir "iyi bir şema tasarla" metni eklemek, taslak prompt'unda zaten
    /// yazan şeyi tekrarlamak ve her istekte boşuna token harcamak olurdu.
    /// </summary>
    public static string For(ProjectArchetype archetype) =>
        Briefs.TryGetValue(archetype, out var brief) ? brief : string.Empty;
}
