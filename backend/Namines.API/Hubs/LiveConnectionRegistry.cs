using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.SignalR;

namespace Namines.API.Hubs;

/// <summary>
/// Bu instance'ta AÇIK olan, kimliği doğrulanmış hub bağlantıları.
///
/// <b>Neden gerekli (docs/last-review M-13 ve M-14):</b> yetki ve oturum iptali
/// yalnızca bağlantı bir hub metodu ÇAĞIRDIĞINDA kontrol ediliyordu
/// (<c>CanvasHub.StillAuthorizedAsync</c>). İki sonucu birden vardı:
///
/// <list type="bullet">
///   <item>
///     <b>M-14:</b> PASİF bir dinleyici hiçbir metot çağırmıyor, dolayısıyla hiç
///     yeniden doğrulanmıyordu. Projeden çıkarılan bir üye, açık bağlantısıyla
///     şema ve imleç yayınlarını almaya <b>süresiz devam ediyordu</b> — yani
///     "üyeyi çıkardım" demek, onun ekranındaki canlı veriyi kesmiyordu.
///   </item>
///   <item>
///     <b>M-13:</b> güvenlik damgası (SecurityStamp) yalnızca HTTP isteklerinde
///     ve SignalR el sıkışmasında doğrulanıyor. Kurulmuş bir WebSocket için jeton
///     bir daha kontrol edilmiyor, yani <b>"tüm oturumları sonlandır" canlı hub'ı
///     kapatmıyordu</b>: parolasını çaldıran kullanıcı çıkış yaptırıyor, saldırgan
///     açık soketten veriyi izlemeye devam ediyordu.
///   </item>
/// </list>
///
/// <b>Neden BELLEKTE ve instance'a özel:</b> kayıt <see cref="HubCallerContext"/>
/// tutuyor, çünkü bir bağlantıyı gerçekten KAPATMANIN
/// (<see cref="HubCallerContext.Abort"/>) tek yolu o. O nesne serileştirilemez,
/// yani Redis backplane'e konamaz. Çok instance'lı dağıtımda her instance kendi
/// bağlantılarını kapatıyor ve bu yeterli: bir bağlantı her zaman TEK bir
/// instance'a bağlıdır.
/// </summary>
public sealed class LiveConnectionRegistry
{
    /// <param name="ProjectId">
    /// Branch odasına katılmışsa projenin kimliği; guest odasındaysa <c>null</c>.
    /// Guest odaları kasıtlı olarak üyelik istemiyor (bkz. CanvasHub sınıf notu),
    /// o yüzden onlar için yetki taraması YAPILMAZ.
    /// </param>
    /// <param name="SecurityStamp">
    /// Bağlantı kurulurken jetonda olan damga. Süpürme bunu kullanıcının GÜNCEL
    /// damgasıyla karşılaştırıyor; damga yoksa (eski jeton) <c>null</c> ve o
    /// bağlantı damga ölçütünden geçirilmiyor — bkz. SecurityStampValidation'ın
    /// "geriye uyumluluk" notu, aynı karar.
    /// </param>
    public sealed record Entry(
        string ConnectionId,
        string UserId,
        string? ProjectId,
        string? SecurityStamp,
        HubCallerContext Context);

    private readonly ConcurrentDictionary<string, Entry> _connections = new();

    public void Add(Entry entry) => _connections[entry.ConnectionId] = entry;

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>
    /// Bağlantının bağlı olduğu projeyi günceller — <c>JoinRoom</c> odayı
    /// değiştirebiliyor ve eski odanın projesiyle taramak yanlış sonuç verirdi.
    /// </summary>
    public void SetProject(string connectionId, string? projectId)
    {
        if (_connections.TryGetValue(connectionId, out var existing))
            _connections[connectionId] = existing with { ProjectId = projectId };
    }

    /// <summary>
    /// Taranacak bağlantılar. Anlık görüntü döner: tarama sürerken bağlantı
    /// açılıp kapanabilir ve sözlüğü kilitleyerek gezmek hub'ı yavaşlatırdı.
    /// </summary>
    public IReadOnlyCollection<Entry> Snapshot() => _connections.Values.ToArray();

    public int Count => _connections.Count;
}
