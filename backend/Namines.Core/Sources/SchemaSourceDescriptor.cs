using System;

namespace Namines.Core.Sources;

/// <summary>
/// Kaynağın kullanıcıyla kurduğu ilişki türü (github/06-EKLENTI-MIMARISI.md §4).
///
/// <b>Ayrım kozmetik değil:</b> <see cref="Connect"/> sürekli bir ilişkidir ve
/// arkadan drift takip edilebilir; <see cref="Import"/> tek seferliktir. Menüde
/// ayrı gruplanmalarının sebebi bu — kullanıcı sonradan "neden drift bildirimi
/// almıyorum" diye sormamalı.
/// </summary>
public enum SchemaSourceKind
{
    Connect,
    Import,
    Starter,
}

/// <summary>
/// Kaynağın ne yapabildiği. UI hiçbir yerde kaynağı ADIYLA tanıyıp yetenek
/// varsaymaz; bayrağa bakar. Yeni kaynak eklendiğinde menü kendiliğinden
/// doğru davranır.
/// </summary>
[Flags]
public enum SchemaSourceCapability
{
    None = 0,
    Import = 1,
    Compare = 2,
    Watch = 4,
    WriteBack = 8,
}

/// <param name="Id">Kararlı kimlik; istemci bunu eylemle eşler.</param>
/// <param name="DisplayName">Menüde görünen ad.</param>
/// <param name="Description">Menüde adın altındaki tek cümle.</param>
/// <param name="Kind">Hangi grupta görüneceği ve hangi sözü verdiği.</param>
/// <param name="Capabilities">Ne yapabildiği — UI bunu okur, kaynağı adıyla tanımaz.</param>
/// <param name="ProducesGuess">
/// Üretilen şema bir çıkarım mı. <b>Katalogda tutulmasının sebebi:</b>
/// "tahmin olduğunu her ekranda söyle" kuralı bugün her kaynağın kendi
/// ekranında ayrı ayrı hatırlanıyor; buradan gelince UI'ın unutma ihtimali
/// kalmıyor.
/// </param>
public sealed record SchemaSourceDescriptor(
    string Id,
    string DisplayName,
    string Description,
    SchemaSourceKind Kind,
    SchemaSourceCapability Capabilities,
    bool ProducesGuess);
