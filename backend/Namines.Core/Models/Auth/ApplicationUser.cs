using Microsoft.AspNetCore.Identity;
using System;

namespace Namines.Core.Models.Auth
{
    public class ApplicationUser : IdentityUser
    {
        public UserType Type { get; set; } = UserType.Individual;
        public string? CompanyName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Extended profile data stored as JSON (bio, social links, location, etc.)
        public string? ProfileJson { get; set; }

        /// <summary>
        /// Gelistirici/sahip hesabi: butun sinirlar kapali (bkz. PlanTier.Dev).
        ///
        /// <b>Neden ayri bir kolon, SubscriptionStatus'e "owner" yazmak degil:</b>
        /// SubscriptionStatus Stripe'in yazdigi bir alan. Bir webhook o degeri
        /// "canceled" yapinca sahip hesabi sessizce Free'ye duserdi ve bunun
        /// nedeni hicbir yerde gorunmezdi. Faturalama durumu ile yetki ayri
        /// seylerdir; ayri kolonda duruyorlar.
        ///
        /// Bu bayragi yalnizca acilista .env'den okunan tohumlama servisi
        /// (DevAccountSeeder) set eder — HicBIR HTTP ucu bunu degistiremez.
        /// </summary>
        public bool IsDev { get; set; }
    }
}
