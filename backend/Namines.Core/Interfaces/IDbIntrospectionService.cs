using Namines.Core.Models;

namespace Namines.Core.Interfaces;

public interface IDbIntrospectionService
{
    /// <summary>
    /// Canlı bir veritabanına bağlanır, INFORMATION_SCHEMA sorgular ve
    /// şemayı Namines DatabaseSchema modeline dönüştürür.
    /// </summary>
    /// <param name="connectionString">Sağlayıcıya özgü connection string.</param>
    /// <param name="dbType">Veri tabanı motoru (MSSQL, PostgreSQL, MySQL, MariaDB, Oracle).</param>
    /// <param name="cancellationToken">İsteği iptal etmek için.</param>
    Task<DatabaseSchema> IntrospectAsync(
        string connectionString,
        string dbType,
        CancellationToken cancellationToken = default);
}
