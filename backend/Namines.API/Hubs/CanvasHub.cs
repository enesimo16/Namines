using System;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Realtime;
using Namines.API.Controllers;
using Namines.Infrastructure.Data;

namespace Namines.API.Hubs;

// NOT: Odalar "paylaşım-linki capability" modeliyle çalışır (tahmin edilemez roomId,
// crypto.randomUUID() ile üretilir). Guest erişimi TASARIM GEREĞİDİR — girişsiz
// kullanıcılar da /canvas sayfasında gerçek zamanlı işbirliği yapabilmeli. Bu yüzden
// hub'a bağlanmak JWT ZORUNLU DEĞİL.
//
// G17 — new-phase/30-SERVER-SIDE-BRANCHING.md §3 Adım 2: kimliği doğrulanmış bir
// kullanıcının aktif bir projesi varsa, frontend artık roomId olarak rastgele bir
// string yerine BranchController.GetOrCreateDefaultBranch'ten dönen gerçek branch
// ID'sini kullanıyor (bkz. frontend hooks/useMultiplayer.ts). Bu sınıf DEĞİŞMEDİ —
// hub, roomId'nin rastgele mi yoksa bir branch ID'si mi olduğunu bilmiyor/bilmesi
// gerekmiyor (IPresenceStore aynı kalır, doc'un öngördüğü tam olarak bu).
//
// !! G17 SONRASI DÜZELTME: "hub roomId'nin ne olduğunu bilmiyor/bilmesi gerekmiyor"
// varsayımı YANLIŞTI. roomId artık kalıcı bir kiracı kaynağı (branch ID) ve her üyeye
// (Viewer dahil) veriliyor; projeden çıkarılan bir üye elindeki ID ile bağlanıp yayınları
// dinlemeye ve şema yazmaya devam edebilirdi. Bu yüzden roomId BİR BRANCH İSE proje
// üyeliği burada zorunlu (JoinRoom: CanView, UpdateSchema: CanEdit, yetki kısa aralıkla
// yeniden kontrol ediliyor). Branch OLMAYAN odalar (guest capability odaları) eskisi gibi.
//
// Ama kimliği doğrulanmış (giriş yapmış) bir kullanıcı bağlanıyorsa, sunum adı için
// istemcinin gönderdiği serbest metin yerine JWT claim'inden gelen gerçek adı
// kullanıyoruz — böylece giriş yapmış bir kullanıcının kimliğine bürünmek (başka
// birinin adını yazıp onun gibi görünmek) mümkün olmuyor. Anonim/guest kullanıcılar
// için davranış değişmedi.
public class CanvasHub : Hub
{
    private readonly IPresenceStore _presence;
    private readonly AuthDbContext _db;
    private readonly LiveConnectionRegistry _liveConnections;

    public CanvasHub(IPresenceStore presence, AuthDbContext db, LiveConnectionRegistry liveConnections)
    {
        _presence = presence;
        _db = db;
        _liveConnections = liveConnections;
    }

    // Bağlantı başına durum (Context.Items): katıldığı odanın branch olup olmadığı ve
    // son yetki kontrolünün zamanı/sonucu. Her imleç hareketinde iki sorgu atmamak için
    // sonuç kısa süre (AuthTtl) önbellekte tutuluyor.
    private const string BranchProjectKey = "canvas.branchProjectId";
    private const string ViewCheckKey = "canvas.viewChecked";
    private const string EditCheckKey = "canvas.editChecked";
    private static readonly TimeSpan AuthTtl = TimeSpan.FromSeconds(15);

    private string? CurrentUserId => Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Kimliği doğrulanmış bağlantıları YETKİ TARAMASINA kaydeder
    /// (<see cref="LiveConnectionAuthSweeper"/>, docs/last-review M-13 ve M-14).
    ///
    /// Anonim/guest bağlantılar kaydedilmiyor: onlar tasarım gereği üyelik
    /// istemiyor (bkz. sınıf notu) ve taranacak bir kimlikleri de yok.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUserId;
        if (!string.IsNullOrEmpty(userId))
        {
            _liveConnections.Add(new LiveConnectionRegistry.Entry(
                ConnectionId: Context.ConnectionId,
                UserId: userId,
                // Oda henüz bilinmiyor; JoinRoom onu güncelliyor.
                ProjectId: null,
                SecurityStamp: Context.User?.FindFirst(AuthController.SecurityStampClaimType)?.Value,
                Context: Context));
        }

        await base.OnConnectedAsync();
    }

    /// <summary>roomId bir branch ise sahibi proje kimliği; değilse (guest odası) null.</summary>
    private Task<string?> ResolveBranchProjectAsync(string roomId) =>
        _db.Branches.AsNoTracking().Where(b => b.Id == roomId).Select(b => b.ProjectId).FirstOrDefaultAsync();

    /// <summary>
    /// Çağıran, YAYIN yaptığı odaya gerçekten katılmış olmalı. Yalnızca roomId biçiminin
    /// geçerli olması yetmiyor: oda adını bilen herkes katılmadan o odaya imleç/şema
    /// basabiliyordu.
    /// </summary>
    private async Task<bool> IsCallerInRoomAsync(string roomId) =>
        await _presence.TryGetRoomAsync(Context.ConnectionId) == roomId;

    /// <summary>
    /// Branch odalarında üyelik (ve isteğe bağlı düzenleme) yetkisini, kısa bir TTL ile
    /// yeniden doğrular. Yetki kaybolmuşsa bağlantı gruptan ÇIKARILIR — kaldırılan bir
    /// üye açık bir bağlantıyla yayın almaya devam edemez. Guest odaları etkilenmez.
    /// </summary>
    private async Task<bool> StillAuthorizedAsync(string roomId, bool requireEdit)
    {
        if (!Context.Items.TryGetValue(BranchProjectKey, out var p) || p is not string projectId)
            return true;

        var key = requireEdit ? EditCheckKey : ViewCheckKey;
        if (Context.Items.TryGetValue(key, out var cached) && cached is ValueTuple<DateTime, bool> entry
            && DateTime.UtcNow - entry.Item1 < AuthTtl)
            return entry.Item2;

        var userId = CurrentUserId;
        var allowed = !string.IsNullOrEmpty(userId) &&
            (requireEdit ? await _db.CanEditAsync(projectId, userId) : await _db.CanViewAsync(projectId, userId));

        Context.Items[key] = (DateTime.UtcNow, allowed);
        if (!allowed) await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomId);
        return allowed;
    }

    private static readonly Regex RoomIdPattern = new(@"^[A-Za-z0-9_\-]{1,100}$", RegexOptions.Compiled);
    private static bool IsValidRoomId(string? roomId) => !string.IsNullOrEmpty(roomId) && RoomIdPattern.IsMatch(roomId);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DatabaseSchema> RoomSchemas = new();

    private string ResolveDisplayName(string clientSuppliedName) =>
        PresenceIdentity.ResolveDisplayName(Context.User, clientSuppliedName);

    public async Task JoinRoom(string roomId, string userName)
    {
        if (!IsValidRoomId(roomId)) return;

        // Önceki odadan kalan branch durumunu temizle; aksi hâlde guest odasına geçen bir
        // bağlantı eski odanın yetki önbelleğini taşırdı.
        Context.Items.Remove(BranchProjectKey);
        Context.Items.Remove(ViewCheckKey);
        Context.Items.Remove(EditCheckKey);

        var projectId = await ResolveBranchProjectAsync(roomId);
        if (projectId is not null)
        {
            var userId = CurrentUserId;
            if (string.IsNullOrEmpty(userId) || !await _db.CanViewAsync(projectId, userId))
                throw new HubException("You do not have access to this project.");

            Context.Items[BranchProjectKey] = projectId;
            Context.Items[ViewCheckKey] = (DateTime.UtcNow, true);
        }

        // Taramanin dogru projeye bakmasi icin: oda degisebiliyor ve eski odanin
        // projesiyle taramak yanlis sonuc verirdi.
        _liveConnections.SetProject(Context.ConnectionId, projectId);

        await _presence.SetRoomAsync(Context.ConnectionId, roomId);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveUserJoined", Context.ConnectionId, ResolveDisplayName(userName));

        if (RoomSchemas.TryGetValue(roomId, out var cachedSchema) && cachedSchema.Tables is { Count: > 0 })
        {
            await Clients.Caller.SendAsync("ReceiveSchema", cachedSchema);
        }
    }

    public async Task MoveCursor(string roomId, string userName, double x, double y)
    {
        if (!IsValidRoomId(roomId)) return;
        if (!await IsCallerInRoomAsync(roomId) || !await StillAuthorizedAsync(roomId, requireEdit: false)) return;
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveCursor", Context.ConnectionId, ResolveDisplayName(userName), x, y);
    }

    public async Task UpdateSchema(string roomId, DatabaseSchema schema)
    {
        if (!IsValidRoomId(roomId)) return;
        if (!await IsCallerInRoomAsync(roomId) || !await StillAuthorizedAsync(roomId, requireEdit: true)) return;
        if (schema?.Tables is { Count: > 0 })
        {
            if (RoomSchemas.Count > 500) RoomSchemas.Clear();
            RoomSchemas[roomId] = schema;
        }
        await Clients.OthersInGroup(roomId).SendAsync("ReceiveSchema", schema);
    }

    // Yalnızca çağıran ile hedefin AYNI odada olması durumunda şema gönderilir
    // (cross-room şema enjeksiyonunu engeller). Üyelik artık IPresenceStore
    // üzerinden okunuyor: Redis yapılandırılmışsa bu kontrol çok instance'lı
    // dağıtımda da doğru çalışır — bellek-içi static dictionary yalnızca
    // kendi instance'ının bağlantılarını görebiliyordu.
    public async Task SendSchemaToPeer(string peerConnectionId, DatabaseSchema schema)
    {
        if (string.IsNullOrEmpty(peerConnectionId)) return;

        var callerRoom = await _presence.TryGetRoomAsync(Context.ConnectionId);
        if (callerRoom is null) return;

        var targetRoom = await _presence.TryGetRoomAsync(peerConnectionId);
        if (targetRoom is null) return;

        if (callerRoom != targetRoom) return;
        if (!await StillAuthorizedAsync(callerRoom, requireEdit: true)) return;
        if (schema?.Tables is { Count: > 0 })
        {
            RoomSchemas[callerRoom] = schema;
        }
        await Clients.Client(peerConnectionId).SendAsync("ReceiveSchema", schema);
    }

    public override async Task OnDisconnectedAsync(System.Exception? exception)
    {
        _liveConnections.Remove(Context.ConnectionId);

        // Peer ayrılınca odadakilere bildir → hayalet imleç kalmaz.
        var roomId = await _presence.RemoveAsync(Context.ConnectionId);
        if (roomId is not null && IsValidRoomId(roomId))
        {
            await Clients.OthersInGroup(roomId).SendAsync("ReceiveUserLeft", Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }
}
