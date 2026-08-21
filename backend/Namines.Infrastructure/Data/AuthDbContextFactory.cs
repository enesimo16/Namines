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

            // Program.cs'teki ile aynı varsayılan — yalnızca migration şemasını üretmek
            // için kullanılıyor, gerçek bağlantı çalışma zamanında appsettings/env'den gelir.
            var connectionString = "Host=localhost;Port=5432;Database=namines_control;Username=postgres;Password=postgres";
            optionsBuilder.UseNpgsql(connectionString);

            return new AuthDbContext(optionsBuilder.Options);
        }
    }
}
