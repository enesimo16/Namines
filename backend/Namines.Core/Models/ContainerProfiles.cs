using System.Collections.Generic;
using Namines.Core.Enums;

namespace Namines.Core.Models;

public class ContainerProfile
{
    public string Image { get; set; } = string.Empty;
    public string Tag { get; set; } = "latest";
    public Dictionary<string, string> EnvVars { get; set; } = new();

    /// <summary>
    /// Motorun ÇALIŞABİLMEK için gerektirdiği en az bellek (bayt).
    ///
    /// <b>Neden profilin parçası ve neden ölçülü bir sayı:</b> SQL Server
    /// 2022 açılışta makinenin belleğine bakıyor ve 2000 MB'ın altındaysa
    /// "This program requires a machine with at least 2000 megabytes of
    /// memory" diyerek KENDİNİ KAPATIYOR. Bu, konteynere verilen
    /// <c>--memory</c> değeriyle değil, Docker VM'inin toplam belleğiyle
    /// ilgili — 1.9 GB'lık bir VM'de 4 GB limit vermek de işe yaramıyor
    /// (ölçüldü).
    ///
    /// Bu bilgi olmadan böyle bir ortamda hata "container hazır olamadı"
    /// diye görünüyordu; yani asıl sebep — ve çözümü — kullanıcıdan gizli
    /// kalıyordu.
    /// </summary>
    public long MinimumMemoryBytes { get; set; }
}

public static class ContainerProfiles
{
    public static ContainerProfile GetProfile(DatabaseType dbType)
    {
        return dbType switch
        {
            DatabaseType.MSSQL => new ContainerProfile
            {
                Image = "mcr.microsoft.com/mssql/server",
                Tag = "2022-latest",
                EnvVars = new Dictionary<string, string>
                {
                    { "ACCEPT_EULA", "Y" },
                    { "MSSQL_SA_PASSWORD", "Namines_Secure123!" },
                    { "MSSQL_PID", "Developer" }
                },
                // SQL Server'in kendi acilis kontrolu: 2000 MB. Bir miktar pay
                // birakiliyor cunku sunucu isletim sisteminin de yer kaplamasini
                // hesaba katiyor.
                MinimumMemoryBytes = 2200L * 1024 * 1024
            },
            DatabaseType.PostgreSQL => new ContainerProfile
            {
                Image = "postgres",
                Tag = "15-alpine",
                EnvVars = new Dictionary<string, string>
                {
                    { "POSTGRES_PASSWORD", "Namines_Secure123!" },
                    { "POSTGRES_USER", "postgres" },
                    { "POSTGRES_DB", "naminesdb" }
                }
            },
            DatabaseType.MySQL => new ContainerProfile
            {
                Image = "mysql",
                Tag = "8.0",
                EnvVars = new Dictionary<string, string>
                {
                    { "MYSQL_ROOT_PASSWORD", "Namines_Secure123!" },
                    { "MYSQL_DATABASE", "naminesdb" }
                }
            },
            _ => throw new System.Exception("Unsupported database type for containerization")
        };
    }
}
