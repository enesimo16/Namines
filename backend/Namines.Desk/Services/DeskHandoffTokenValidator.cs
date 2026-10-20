using System.Security.Cryptography;
using System.Text;
using Namines.Core.Models.Auth;
using Namines.Desk.Abstractions;

namespace Namines.Desk.Services
{
    public class DeskHandoffTokenValidator : IDeskHandoffTokenValidator
    {
        public string HashToken(string rawToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        public bool IsExpired(DeskHandoffToken token)
        {
            return token.UsedAt != null || token.ExpiresAt <= DateTime.UtcNow;
        }
    }
}
