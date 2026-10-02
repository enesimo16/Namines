using Namines.API.Controllers;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// <c>POST /api/auth/sync</c> — istemci-seçimli YENİ proje kimliğinin biçimi.
///
/// <b>Bulunan açık:</b> kimlik düz bir <c>string</c> idi, hiçbir biçim kısıtı yoktu ve aşağı akışta
/// dört yere akıyordu: Vault dosya yolu, Postgres veritabanı/rol adı (kayıplı temizleme:
/// <c>7f3a-b21c</c> ve <c>7f3a_b21c</c> aynı adı veriyor → başka bir kiracının veritabanının
/// devralınması) ve Neon/Supabase proje adları.
/// </summary>
public class SyncProjectIdValidationTests
{
    [Theory]
    [InlineData("7f3ab21c-9d44-4e0a-8b1e-2c5d6f7a8b90")]   // crypto.randomUUID()
    [InlineData("7F3AB21C-9D44-4E0A-8B1E-2C5D6F7A8B90")]
    [InlineData("ground-test")]
    [InlineData("a")]
    public void Ordinary_client_ids_are_accepted(string id)
    {
        Assert.True(AuthController.IsSafeNewProjectId(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData("../vault-backups-evil")]
    [InlineData("..%5Cvault-backups-evil")]
    [InlineData("a/b")]
    [InlineData("a.b")]                                     // nokta: yol ve tanımlayıcı sürprizi
    [InlineData("7f3ab21c_9d44_4e0a_8b1e_2c5d6f7a8b90")]   // alt çizgi: bir GUID'in temizlenmiş adına çakışırdı
    [InlineData("id with space")]
    [InlineData("id\nnewline")]
    public void Ids_that_could_escape_a_path_or_collide_with_an_identifier_are_rejected(string? id)
    {
        Assert.False(AuthController.IsSafeNewProjectId(id));
    }

    [Fact]
    public void An_overlong_id_is_rejected()
    {
        Assert.False(AuthController.IsSafeNewProjectId(new string('a', 65)));
        Assert.True(AuthController.IsSafeNewProjectId(new string('a', 64)));
    }
}
