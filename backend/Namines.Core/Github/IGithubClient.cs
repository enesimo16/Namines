using System.Threading;
using System.Threading.Tasks;

namespace Namines.Core.Github;

/// <param name="Owner">Depo sahibi (<c>acme</c>).</param>
/// <param name="Name">Depo adı (<c>shop</c>).</param>
public sealed record GithubRepository(string Owner, string Name)
{
    public override string ToString() => $"{Owner}/{Name}";
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
        GithubRepository repository, long installationId, string path, string reference,
        CancellationToken cancellationToken = default);
}
