using System;
using Namines.Core.Enums;

namespace Namines.Core.Models;

/// <summary>
/// Bir branch için ayağa kaldırılmış canlı veritabanı (06 §4).
/// </summary>
/// <param name="BranchId">Sahibi branch.</param>
/// <param name="Engine">Motor.</param>
/// <param name="Host">Her zaman <c>127.0.0.1</c> — bkz. <see cref="ConnectionString"/>.</param>
/// <param name="Port">Host üzerinde yayımlanan port.</param>
/// <param name="Database">Veritabanı adı.</param>
/// <param name="Username">Kullanıcı adı.</param>
/// <param name="Password">Branch'e özel, rastgele üretilmiş parola.</param>
/// <param name="ExpiresAt">
/// Bu andan sonra süpürülür. Zaman aşımı olmadan branch veritabanları birikip
/// host'u doldurur — geliştirici her açtığı branch'i kapatmayı hatırlamaz.
/// </param>
public sealed record BranchDatabase(
    string BranchId,
    DatabaseType Engine,
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    DateTime ExpiresAt)
{
    /// <summary>
    /// Bağlantı dizesi.
    ///
    /// Host HER ZAMAN loopback'tir çünkü container portu bilinçli olarak yalnızca
    /// <c>127.0.0.1</c>'e yayımlanır. <c>0.0.0.0</c>'a yayımlamak, bilinen bir
    /// kullanıcı adıyla çalışan bir veritabanını makinenin bulunduğu her ağa
    /// açardı; bir geliştirme aracının varsayılan olarak yapabileceği en kötü şey.
    /// </summary>
    public string ConnectionString => Engine switch
    {
        DatabaseType.PostgreSQL =>
            $"Host={Host};Port={Port};Database={Database};Username={Username};Password={Password}",
        DatabaseType.MySQL =>
            $"Server={Host};Port={Port};Database={Database};Uid={Username};Pwd={Password}",
        DatabaseType.MSSQL =>
            $"Server={Host},{Port};Database={Database};User Id={Username};Password={Password};TrustServerCertificate=True",
        _ => throw new NotSupportedException($"No connection string format for {Engine}."),
    };
}
