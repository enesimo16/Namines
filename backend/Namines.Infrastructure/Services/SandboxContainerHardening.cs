using System.Collections.Generic;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Veritabanı imajlarından (postgres, mysql, mssql) açılan atılabilir container'ların
/// ortak sertleştirme değerleri. Sertleştirilmiş sandbox (<see cref="DockerBackupService"/>)
/// ve "Run Tests"/branch container'ları AYNI değerleri kullansın diye tek yerde:
/// biri güncellenip öteki unutulursa aynı DDL iki yolda farklı yetkiyle çalışırdı.
/// </summary>
internal static class SandboxContainerHardening
{
    /// <summary>Fork bombası ve kaçak süreç zinciri: bellek/CPU limiti bunları durdurmuyor.</summary>
    public const long PidsLimit = 512;

    /// <summary>
    /// SQL Server açılışta yüzlerce iş parçacığı açıyor ve pids sınırı thread'leri de
    /// sayıyor: 512'de motor tempdb'den sonra sessizce takılıp hazırlık zaman aşımına
    /// düşüyordu (CI, Namines.Tests.RunTests MSSQL testleri). Sınır kalkmıyor, yükseliyor.
    /// </summary>
    public const long MssqlPidsLimit = 4096;

    public static long PidsLimitFor(Namines.Core.Enums.DatabaseType engine) =>
        engine == Namines.Core.Enums.DatabaseType.MSSQL ? MssqlPidsLimit : PidsLimit;

    /// <summary>
    /// Docker'ın VARSAYILAN kümesinde olup bu iş için GEREKSİZ yetkiler.
    ///
    /// Liste bilerek kısa: veritabanı imajları ilk açılışta dosya sahipliği değiştirip
    /// kullanıcı düşürüyor (CHOWN, SETUID, SETGID, FOWNER, DAC_OVERRIDE), onları düşürmek
    /// container'ı hiç başlatmazdı. Bkz. <see cref="DockerBackupService"/> doğrulama notu:
    /// postgres:15-alpine ve mysql:8.0 bu küme düşürülmüş hâlde gerçek container olarak açıldı.
    /// </summary>
    public static List<string> CapDrop() => new() { "NET_RAW", "MKNOD", "AUDIT_WRITE", "SETFCAP" };
}
