using System.Formats.Tar;
using System.IO;
using System.Text;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Docker'ın <c>ExtractArchiveToContainerAsync</c> API'si bir tar akışı bekler;
/// tek bir metin dosyasını container'a kopyalamak için gereken tek şey bu.
///
/// <see cref="BranchTestRunnerService"/> ve <see cref="BranchDatabaseProvisioner"/>
/// aynı işi yapıyordu; tek kopyada tutuluyor — bu kod tabanı aynı mantığı
/// kopyalamanın bedelini daha önce ödedi (6 controller'da tekrarlanan yetki
/// kontrolü → OrgAccess).
/// </summary>
internal static class DockerTarFile
{
    public static Stream SingleFile(string fileName, string content)
    {
        var memoryStream = new MemoryStream();
        using (var archive = new TarWriter(memoryStream, TarEntryFormat.Pax, leaveOpen: true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, fileName)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
            };
            archive.WriteEntry(entry);
        }
        memoryStream.Position = 0;
        return memoryStream;
    }
}
