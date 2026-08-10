using System.Security.Claims;
using Namines.Core.Realtime;

namespace Namines.Tests.Realtime;

/// <summary>
/// CanvasHub'ın sunum-adı çözümleme kuralı: kimliği doğrulanmış kullanıcı için
/// istemcinin gönderdiği ada GÜVENİLMEZ, JWT claim'i kullanılır — aksi halde
/// giriş yapmış bir kullanıcının adını yazıp onun gibi görünmek (kimlik taklidi)
/// mümkün olurdu. Anonim/guest kullanıcı için davranış (Studio'da girişsiz
/// işbirliği tasarım gereği desteklenir) değişmedi.
/// </summary>
public class PresenceIdentityTests
{
    private static ClaimsPrincipal AuthenticatedUser(string name) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name)],
            authenticationType: "TestAuth")); // authenticationType boş olmamalı, aksi halde IsAuthenticated=false olur

    private static ClaimsPrincipal AnonymousUser() =>
        new(new ClaimsIdentity()); // authenticationType YOK → IsAuthenticated=false

    [Fact]
    public void Authenticated_user_gets_claim_name_not_client_supplied_name()
    {
        var user = AuthenticatedUser("Gerçek Ayşe");

        // Saldırgan senaryosu: istemci "Yönetici" diye kendini tanıtmaya çalışıyor.
        var result = PresenceIdentity.ResolveDisplayName(user, "Yönetici");

        Assert.Equal("Gerçek Ayşe", result);
    }

    [Fact]
    public void Anonymous_user_gets_client_supplied_name()
    {
        var user = AnonymousUser();

        var result = PresenceIdentity.ResolveDisplayName(user, "Designer-1234");

        Assert.Equal("Designer-1234", result);
    }

    [Fact]
    public void Null_principal_falls_back_to_client_supplied_name()
    {
        // Context.User null olabilir (ör. bazı test/host senaryolarında) — çökmemeli.
        var result = PresenceIdentity.ResolveDisplayName(null, "Guest-42");

        Assert.Equal("Guest-42", result);
    }

    [Fact]
    public void Authenticated_user_without_name_claim_falls_back_to_client_supplied_name()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, "x@example.com")], "TestAuth");
        var user = new ClaimsPrincipal(identity);

        var result = PresenceIdentity.ResolveDisplayName(user, "FallbackName");

        Assert.Equal("FallbackName", result);
    }

    [Fact]
    public void Long_names_are_truncated_to_max_length()
    {
        var longName = new string('a', 200);

        var result = PresenceIdentity.ResolveDisplayName(AnonymousUser(), longName, maxLength: 64);

        Assert.Equal(64, result.Length);
    }

    [Fact]
    public void Null_client_supplied_name_returns_empty_not_exception()
    {
        var result = PresenceIdentity.ResolveDisplayName(AnonymousUser(), null);

        Assert.Equal(string.Empty, result);
    }
}
