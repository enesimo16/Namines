using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Namines.Core.Models.Auth;

namespace Namines.API.Services;

/// <summary>
/// Açılışta .env'den okunan dev hesabını (görünmeyen, sınırsız) oluşturur ya da tazeler.
///
/// <b>Çözdüğü sorun:</b> geliştirme sırasında açılan hesaplar unutuluyordu — her
/// yeniden kurulumda yeni bir e-posta uyduruluyor, eskisinin şifresi kayboluyordu.
/// Artık tek doğru kaynak <c>.env</c>: orada ne yazıyorsa hesap odur.
///
/// <b>Kimlik bilgileri koda ya da yapılandırma dosyasına YAZILMAZ.</b> Yalnızca
/// <c>.env</c>'den okunuyor ve <c>.env</c> <c>.gitignore</c>'da — bu dosya depoya
/// hiç girmiyor. <c>appsettings.json</c>'a koymak, sırrı doğrudan GitHub'a
/// göndermek olurdu.
///
/// <b>Yapılandırma yoksa HİÇBİR ŞEY yapmaz.</b> Kod içine gömülü bir varsayılan
/// parola, üretimde herkesin bildiği bir arka kapıya dönüşürdü.
/// </summary>
public static class DevAccountSeeder
{
    public static async Task SeedAsync(
        UserManager<ApplicationUser> users,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct = default)
    {
        var email = configuration["Dev:Email"];
        var password = configuration["Dev:Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogDebug("Dev:Email/Dev:Password tanımlı değil, dev hesabı tohumlanmadı.");
            return;
        }

        var user = await users.FindByEmailAsync(email);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                // Corporate: arayuzdeki eski ikili rozet (Header.tsx, "FREE/PRO
                // MEMBER") hala bu alana bakiyor, planTier'a degil. Individual
                // birakilirsa dev hesabi her yerde "Free" gorunurdu.
                Type = UserType.Corporate,
                IsDev = true,
                CreatedAt = DateTime.UtcNow,
            };

            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                // Fırlatmıyoruz: dev hesabı bir kolaylık, uygulamanın çalışma
                // şartı değil. Parola politikası tutmadığı için uygulamanın
                // tamamen açılmaması orantısız olurdu.
                logger.LogError("Dev hesabı oluşturulamadı: {Errors}",
                    string.Join("; ", created.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogWarning("Dev hesabı oluşturuldu: {Email} (sınırsız kota, digerlerine gorunmez).", email);
            return;
        }

        // Hesap zaten var. .env'deki parola TEK DOĞRU KAYNAK: unutulan parolayı
        // kurtarmanın yolu .env'i açıp bakmak olmalı, veritabanına elle müdahale
        // etmek değil. Bu yüzden parola eşleşmiyorsa .env'dekine çekiliyor.
        if (!await users.CheckPasswordAsync(user, password))
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var reset = await users.ResetPasswordAsync(user, token, password);

            if (reset.Succeeded)
                logger.LogWarning("Dev hesabının parolası .env'deki değere çekildi: {Email}", email);
            else
                logger.LogError("Dev hesabının parolası güncellenemedi: {Errors}",
                    string.Join("; ", reset.Errors.Select(e => e.Description)));
        }

        if (!user.IsDev || !user.EmailConfirmed || user.Type != UserType.Corporate)
        {
            user.IsDev = true;
            user.EmailConfirmed = true;
            user.Type = UserType.Corporate;
            await users.UpdateAsync(user);
            logger.LogWarning("Var olan hesap dev olarak işaretlendi: {Email}", email);
        }
    }
}
