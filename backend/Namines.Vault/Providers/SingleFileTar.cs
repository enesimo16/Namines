using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.Vault.Providers;

/// <summary>
/// Tek dosyalık bir tar akışı üretir.
///
/// <b>Neden gerekti:</b> Docker'ın "konteynere dosya koy" API'si (archive)
/// yalnızca tar kabul ediyor. Bütün bir tar kütüphanesi çekmek, tek bir
/// dosyalık bir başlık için orantısız bir bağımlılık olurdu.
///
/// .NET 8'in <c>System.Formats.Tar</c>'ı da var; burada elle yazılmasının
/// sebebi, girdi boyutunun ÖNCEDEN bilinmesi zorunluluğu ve akışı hiç belleğe
/// almadan geçirme ihtiyacı — ikisi de bu 60 satırla tam olarak karşılanıyor.
/// </summary>
internal static class SingleFileTar
{
    private const int BlockSize = 512;

    /// <summary>
    /// <paramref name="content"/>'i <paramref name="fileName"/> adıyla tek girdili
    /// bir tar olarak <paramref name="destination"/>'a yazar.
    /// </summary>
    /// <param name="size">
    /// İçeriğin TAM boyutu. Tar başlığı içeriğin önünde gider, dolayısıyla boyut
    /// önceden bilinmek zorunda — bu yüzden çağıran akışı değil, uzunluğu bilinen
    /// bir kaynağı vermeli.
    /// </param>
    public static async Task WriteAsync(
        Stream destination, string fileName, Stream content, long size, CancellationToken ct)
    {
        await destination.WriteAsync(BuildHeader(fileName, size), ct);
        await content.CopyToAsync(destination, ct);

        // İçerik 512'nin katına tamamlanır, ardından iki boş blok dosya sonunu
        // işaretler — ikisi de tar biçiminin şartı.
        var remainder = (int)(size % BlockSize);
        if (remainder != 0)
            await destination.WriteAsync(new byte[BlockSize - remainder], ct);

        await destination.WriteAsync(new byte[BlockSize * 2], ct);
    }

    private static byte[] BuildHeader(string fileName, long size)
    {
        var header = new byte[BlockSize];

        WriteText(header, 0, 100, fileName);
        WriteOctal(header, 100, 8, 0b110_100_100);            // mode 0644
        WriteOctal(header, 108, 8, 0);                         // uid
        WriteOctal(header, 116, 8, 0);                         // gid
        WriteOctal(header, 124, 12, size);
        WriteOctal(header, 136, 12, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        header[156] = (byte)'0';                               // normal dosya
        WriteText(header, 257, 6, "ustar\0");
        WriteText(header, 263, 2, "00");

        // Sağlama, sağlama alanı BOŞLUKLARLA doluyken hesaplanır — biçimin kuralı.
        for (var i = 148; i < 156; i++) header[i] = (byte)' ';
        var checksum = 0;
        foreach (var b in header) checksum += b;
        WriteOctal(header, 148, 7, checksum);
        header[155] = (byte)' ';

        return header;
    }

    private static void WriteText(byte[] buffer, int offset, int length, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        if (bytes.Length > length)
            throw new ArgumentException($"'{value}' does not fit in {length} bytes.", nameof(value));

        Array.Copy(bytes, 0, buffer, offset, bytes.Length);
    }

    /// <summary>Tar sayıları NUL ile biten, sıfırla soldan doldurulmuş sekizlik metindir.</summary>
    private static void WriteOctal(byte[] buffer, int offset, int length, long value)
    {
        var text = Convert.ToString(value, 8).PadLeft(length - 1, '0');
        WriteText(buffer, offset, length - 1, text);
    }
}
