using System;
using System.Security.Cryptography;
using System.Text;

namespace Namines.Core.Models.Auth;

/// <summary>
/// Paylaşım token'ının arama özeti: SHA-256, küçük harf hex. Postgres'teki
/// <c>encode(sha256(convert_to("ShareToken",'UTF8')),'hex')</c> ile BİREBİR aynı çıktıyı
/// verir (migration geriye dönük doldurma bunu kullanıyor). Token 192-bit rastgele olduğu için
/// yavaş KDF gerekmez (API anahtarı özetiyle aynı gerekçe).
/// </summary>
public static class ShareTokenHasher
{
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
