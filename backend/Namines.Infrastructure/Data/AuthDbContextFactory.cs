using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Namines.Infrastructure.Data
{
    /// <summary>
    /// `dotnet ef migrations add` için tasarım-zamanı fabrikası.
    ///
    /// Neden gerekli: bu proje kendi başına çalıştırılabilir değil (Namines.API
    /// startup project'i normalde bu rolü üstlenir), ama Namines.API'yi build etmek
    /// geliştiricinin o an çalışan dev sunucusuyla (bin/ kilidi) çakışabilir. Bu
    /// fabrika, `dotnet ef` komutunun --startup-project olmadan doğrudan
    /// Namines.Infrastructure üzerinde çalışmasını sağlar — yalnızca migration
    /// ÜRETİMİ için kullanılır, çalışma zamanı bağlantısını etkilemez (o hâlâ
    /// Namines.API/Program.cs'teki DI kaydından gelir).
    /// </summary>
    public class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
    {
        public AuthDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<AuthDbContext>();

            // Migration ÜRETİMİ için bağlantının doğru olması gerekmez (şema koddan
            // türetilir), ama `dotnet ef database update` AYNI fabrikayı kullanıyor —
            // orada gerçekten bağlanır.
            //
            // <b>Düzeltilen hata:</b> burada `postgres/postgres` sabit kodluydu, oysa
            // docker-compose'daki control DB `namines` kullanıcısıyla açılıyor
            // (`namines-control-db` servisi). Sonuç: `database update` her seferinde
            // "password authentication failed for user postgres" veriyordu ve
            // migration'lar elle uygulanmak zorunda kalıyordu.
            //
            // Öncelik sırası: ortam değişkeni → appsettings ile aynı varsayılan.
            var connectionString =
                Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? "Host=localhost;Port=5432;Database=namines_control;Username=namines;Password=namines_dev_only_change_in_prod";
            optionsBuilder.UseNpgsql(connectionString);

            return new AuthDbContext(optionsBuilder.Options);
        }
    }
}
