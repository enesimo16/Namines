using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.Infrastructure.Services;

namespace Namines.Tests.Services;

/// <summary>
/// Yedekleme iş kuyruğu (PERF-003 / B-35).
///
/// <b>Kuyruğun var olma sebebi:</b> Yedekleme süresi veritabanı boyutuyla
/// doğrusal büyüyor. 50 GB'lık bir veritabanında HTTP isteği dakikalarca açık
/// kalır; araya giren her proxy zaman aşımı bunu keser ve kullanıcı "yedek
/// başarısız" görür — oysa yedek sunucuda devam etmektedir.
///
/// <b>Neden <c>Task.Run</c> değil:</b> İstek kapsamındaki <c>DbContext</c>
/// yanıt döndüğünde atılıyor; ona arka planda dokunmak
/// <c>ObjectDisposedException</c> demek — ve bu yalnızca YAVAŞ yedeklerde,
/// yani tam olarak üretimde görünür. Kuyruğu tüketen işçi kendi kapsamını
/// açıyor.
/// </summary>
public class VaultJobQueueTests
{
    private static VaultBackupJob Job(string id) => new(id, "proj-1");

    [Fact]
    public async Task Kuyruga_eklenen_is_sirayla_geri_aliniyor()
    {
        var queue = new VaultJobQueue();

        Assert.True(queue.TryEnqueue(Job("a")));
        Assert.True(queue.TryEnqueue(Job("b")));

        Assert.Equal("a", (await queue.DequeueAsync(CancellationToken.None)).BackupId);
        Assert.Equal("b", (await queue.DequeueAsync(CancellationToken.None)).BackupId);
    }

    [Fact]
    public void Bekleyen_sayisi_ekleyince_artiyor()
    {
        var queue = new VaultJobQueue();

        Assert.Equal(0, queue.PendingCount);
        queue.TryEnqueue(Job("a"));
        queue.TryEnqueue(Job("b"));

        Assert.Equal(2, queue.PendingCount);
    }

    [Fact]
    public async Task Bekleyen_sayisi_alinca_azaliyor()
    {
        var queue = new VaultJobQueue();
        queue.TryEnqueue(Job("a"));

        await queue.DequeueAsync(CancellationToken.None);

        Assert.Equal(0, queue.PendingCount);
    }

    /// <summary>
    /// Kuyruk DOLDUĞUNDA <c>TryEnqueue</c> <b>beklemeden</b> false dönmeli.
    ///
    /// Beklemesi, HTTP ucunun 503 dönebilmesini imkânsız kılardı: istek
    /// kuyrukta yer açılana kadar askıda kalırdı — yani tam olarak kaçınmaya
    /// çalıştığımız şey. Sessizce sıraya sokup dakikalar sonra başlamak,
    /// "şu an meşgul" demekten daha kötü.
    /// </summary>
    [Fact]
    public void Kuyruk_dolunca_BEKLEMEDEN_reddediyor()
    {
        var queue = new VaultJobQueue();

        // Kapasite 32; fazlası reddedilmeli.
        var accepted = Enumerable.Range(0, 40).Count(i => queue.TryEnqueue(Job($"j{i}")));

        Assert.Equal(32, accepted);
        Assert.False(queue.TryEnqueue(Job("bir-fazla")));
    }

    /// <summary>
    /// Reddedilen iş kuyruğa GİRMEMELİ: sayaç kapasiteyi aşarsa, dolu bir
    /// kuyruk "daha da dolu" görünür ve teşhis yanıltıcı olur.
    /// </summary>
    [Fact]
    public void Reddedilen_is_sayaca_yazilmiyor()
    {
        var queue = new VaultJobQueue();
        for (var i = 0; i < 40; i++) queue.TryEnqueue(Job($"j{i}"));

        Assert.Equal(32, queue.PendingCount);
    }

    /// <summary>
    /// Boş kuyrukta <c>DequeueAsync</c> BEKLEMELİ (dönmemeli). İşçi döngüsü
    /// buna dayanıyor; hemen dönseydi meşgul bir döngüye girip CPU yakardı.
    /// </summary>
    [Fact]
    public async Task Bos_kuyrukta_bekliyor()
    {
        var queue = new VaultJobQueue();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await queue.DequeueAsync(cts.Token));
    }

    /// <summary>
    /// İş sonradan gelirse bekleyen çağrı onu almalı — kuyruğun asıl işi bu.
    /// </summary>
    [Fact]
    public async Task Sonradan_gelen_is_bekleyen_cagriya_ulasiyor()
    {
        var queue = new VaultJobQueue();

        var pending = queue.DequeueAsync(CancellationToken.None);
        queue.TryEnqueue(Job("gec-gelen"));

        Assert.Equal("gec-gelen", (await pending).BackupId);
    }

    [Fact]
    public void Null_is_reddediliyor()
    {
        var queue = new VaultJobQueue();

        Assert.Throws<ArgumentNullException>(() => queue.TryEnqueue(null!));
    }
}
