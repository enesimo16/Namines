using Namines.Core.Models.Auth;
using Namines.Desk.Domain;

namespace Namines.Desk.Abstractions
{
    /// <summary>
    /// Namines Desk v2 SSO devir jetonlarını doğrulayan ve yöneten sözleşme.
    /// </summary>
    public interface IDeskHandoffTokenValidator
    {
        string HashToken(string rawToken);
        bool IsExpired(DeskHandoffToken token);
    }
}
