using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Namines.BuildingBlocks.Contracts;
using Namines.BuildingBlocks.Persistence;
using Namines.Core.Models.Auth;
using Namines.Identity.Application;
using Namines.Identity.Infrastructure.Persistence;
using Namines.Identity.Infrastructure.Security;

namespace Namines.Identity.DependencyInjection;

/// <summary>
/// Identity modülünün DI kaydı: bağlam, ASP.NET Identity, şifreli kullanıcı deposu ve sözleşmeler.
/// API, <c>AuthDbContext</c>'i hiç görmez; Identity yapılandırması da buradan çıkar.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>Auth zinciri her zaman ilk sırada: tarihsel migration'lar tabloları <c>public</c>'te kurar, modül zincirleri onları oradan alır.</summary>
    public const int MigrationOrder = 0;

    public static ModuleMigrationDescriptor MigrationDescriptor { get; } = new(
        "Auth",
        MigrationOrder,
        "public",
        ModuleMigrationOwnership.OwnsChain,
        connectionString => new AuthDbContext(
            new DbContextOptionsBuilder<AuthDbContext>().UseNpgsql(connectionString).Options));

    /// <summary>
    /// <paramref name="configureDbContext"/> verilirse (testler) bağlam onunla kurulur; yoksa üretim ayarları
    /// (geçici hata yeniden denemesi, komut zaman aşımı) kullanılır.
    /// </summary>
    public static IdentityBuilder AddIdentityModule(
        this IServiceCollection services,
        string connectionString,
        Action<IdentityOptions> configureIdentity,
        Action<DbContextOptionsBuilder>? configureDbContext = null)
    {
        if (configureDbContext is not null)
        {
            services.AddDbContext<AuthDbContext>(configureDbContext);
        }
        else
        {
            var commandTimeout = new Npgsql.NpgsqlConnectionStringBuilder(connectionString).CommandTimeout;
            services.AddDbContext<AuthDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.CommandTimeout(commandTimeout);
                    npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null);
                }));
        }

        services.AddSingleton(MigrationDescriptor);
        services.AddScoped<ITransactionParticipant>(sp => new TransactionParticipant(sp.GetRequiredService<AuthDbContext>()));

        services.AddScoped<IIdentityData>(sp => sp.GetRequiredService<AuthDbContext>());
        services.AddScoped<IIdentityDirectory, IdentityDirectoryService>();
        services.AddScoped<IOrganizationMembers, OrganizationMembersService>();
        services.AddScoped<IAccountOrganizations, AccountOrganizationsService>();
        services.AddScoped<IIdentityMaintenance, IdentityMaintenanceService>();
        services.AddScoped<IIdentityAdmin, IdentityAdminService>();
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<IdentityAccountService>();
        services.AddScoped<IIdentityAccounts>(sp => sp.GetRequiredService<IdentityAccountService>());
        services.AddScoped<IAdminUserService>(sp => sp.GetRequiredService<IdentityAccountService>());
        services.AddScoped<Namines.BuildingBlocks.Security.IPlatformAdminAuthority, PlatformAdminAuthority>();

        return services.AddIdentity<ApplicationUser, IdentityRole>(configureIdentity)
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddUserStore<EncryptedUserStore>()
            .AddDefaultTokenProviders();
    }
}
