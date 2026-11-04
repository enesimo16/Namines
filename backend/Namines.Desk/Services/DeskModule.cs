using System;
using Namines.Desk.Abstractions;
using Namines.Desk.Application;

namespace Namines.Desk.Services
{
    /// <summary>
    /// Namines Desk modülü facade implementasyonu.
    /// </summary>
    public sealed class DeskModule : IDeskModule
    {
        public IDeskHandoffTokenValidator TokenValidator { get; }
        public IDeskApplicationService Application { get; }

        public DeskModule(
            IDeskHandoffTokenValidator tokenValidator,
            IDeskApplicationService application)
        {
            TokenValidator = tokenValidator ?? throw new ArgumentNullException(nameof(tokenValidator));
            Application = application ?? throw new ArgumentNullException(nameof(application));
        }
    }
}
