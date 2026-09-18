using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.Core.Github;

/// <param name="Owner">Depo sahibi (<c>acme</c>).</param>
/// <param name="Name">Depo adı (<c>shop</c>).</param>
public sealed record GithubRepository(string Owner, string Name)
{
    public override string ToString() => $"{Owner}/{Name}";
}

/// <param name="Paths">Ağaçtaki dosya (blob) yolları.</param>
/// <param name="Truncated">
/// GitHub ağacı kesti mi. <b>Taşınması şart:</b> kesilmiş bir ağaçta
/// "deponda şema dosyası yok" demek yanlış olur — bakmadığımız bir kısım var.
/// </param>
public sealed record RepositoryTree(IReadOnlyList<string> Paths, bool Truncated);

/// <summary>
/// Depo yok ya da elimizdeki kimlikle görünmüyor.
///
/// <b>İkisi ayrı tip değil, çünkü GitHub ikisini ayırt ETTİRMİYOR</b> —
/// göremediğimiz depoya da 404 döner (varlığını sızdırmamak için). Uydurmak
/// yerine mesaj iki ihtimali birlikte söylüyor.
/// </summary>
public sealed class GithubRepositoryUnavailableException : Exception
{
    public GithubRepositoryUnavailableException(string message) : base(message) { }
}

/// <summary>Anonim okumanın saatlik sınırı doldu — kullanıcıya ne yapacağı söylenebilsin diye ayrı tip.</summary>
public sealed class GithubRateLimitedException : Exception
{
    public GithubRateLimitedException(string message) : base(message) { }
}

/// <summary>
/// Namines Bot'un GitHub'a YAZAN yüzü (11 §7).
///
/// <b>Arayüz, çağıranı kimlik bilgilerinden ayırmak için var.</b> Bot'un mantığı
/// (etki analizi, yorum metni, komut ayrıştırma) hiçbir hesap gerektirmez ve
/// tamamen test edilebilir; yalnızca bu arayüzün ardındaki HTTP çağrıları bir
/// GitHub App ister. İkisini ayırmak, App gelmeden önce her şeyin yazılıp
/// doğrulanabilmesini sağladı.
///
/// Kimlik bilgisi yapılandırılmamışsa <see cref="IsConfigured"/> false döner ve
/// çağıran <b>yazmayı denemez</b> — sahte bir başarı raporlamak, çalıştığı
/// sanılan ama hiçbir şey yapmayan bir özellik bırakırdı.
/// </summary>
public interface IGithubClient
{
    /// <summary>App kimlik bilgileri tanımlı mı?</summary>
    bool IsConfigured { get; }

    /// <summary>Bir PR'a (issue) yorum bırakır.</summary>
    Task PostCommentAsync(
        GithubRepository repository, long installationId, int issueNumber, string body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir commit üzerinde status check oluşturur.
    ///
    /// <paramref name="conclusion"/> <c>failure</c> ise merge korumaları devreye
    /// girer — özelliğin tüm amacı budur, o yüzden değeri
    /// <see cref="PullRequestReviewComposer.ConclusionFor"/> belirler.
    /// </summary>
    Task CreateCheckRunAsync(
        GithubRepository repository, long installationId, string headSha,
        string name, string conclusion, string title, string summary, string body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir dosyanın belirli bir ref'teki içeriğini okur; dosya yoksa <c>null</c>.
    ///
    /// PR'daki şema değişikliğini görmek için gerekli: taban ve baş ref'teki
    /// <c>.nsl</c> dosyalarını okuyup aradaki farkı analiz ediyoruz.
    /// </summary>
    Task<string?> GetFileContentAsync(
        GithubRepository repository, long? installationId, string path, string reference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deponun varsayılan dalı; okunamazsa <c>null</c>.
    ///
    /// Kullanıcı dal seçmediğinde "main" varsaymak, varsayılanı "master" ya da
    /// "develop" olan depolarda sessizce boş sonuç üretirdi.
    /// </summary>
    Task<string?> GetDefaultBranchAsync(
        GithubRepository repository, long? installationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir ref'teki bütün dosya yollarını TEK çağrıda verir
    /// (github/01-DEPO-TARAMA.md).
    ///
    /// <paramref name="installationId"/> <c>null</c> ise istek ANONİM gider ve
    /// public depo okunur — F1'in GitHub App'i beklememesinin sebebi bu.
    /// </summary>
    Task<RepositoryTree> GetRepositoryTreeAsync(
        GithubRepository repository, string reference, long? installationId,
        CancellationToken cancellationToken = default);
}
