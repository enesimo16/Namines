using System;
using Namines.Desk.Abstractions;

namespace Namines.Desk.Services
{
    /// <summary>
    /// Namines Desk modülü facade implementasyonu.
    /// </summary>
    public sealed class DeskModule : IDeskModule
    {
        public IDeskHandoffTokenValidator TokenValidator { get; }

        public DeskModule(IDeskHandoffTokenValidator tokenValidator)
        {
            TokenValidator = tokenValidator ?? throw new ArgumentNullException(nameof(tokenValidator));
        }
    }
}
