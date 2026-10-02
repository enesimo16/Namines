using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.API.Controllers;
using Namines.API.Services;
using Xunit;

namespace Namines.Tests.Controllers;

/// <summary>
/// <see cref="CoderAIController"/> — indirme/akış uçları.
///
/// <b>Bulunan açık:</b> <c>DownloadZip(jobId)</c> route değerini doğrudan
/// <c>Path.Combine(cwd, "Outputs", $"coderai_{jobId}.zip")</c>'e veriyordu. <c>%5C</c> bir
/// yol ayırıcı olmadığı için <c>..\..\appsettings.json</c> tek bir <c>{jobId}</c> olarak
/// bağlanıyor, <c>Path.Combine</c> normalize edip Outputs'tan çıkıyordu. Kardeşi
/// <c>DockerController</c> tam bu saldırıyı yorumunda adıyla anlatıp GUID + sahiplik kontrolü
/// yapıyordu; CoderAI o korumayı hiç almamıştı.
/// </summary>
public sealed class CoderAiDownloadAuthorizationTests : IDisposable
{
    private readonly DockerJobManager _jobs = new(NullLogger<DockerJobManager>.Instance);
    private readonly List<string> _created = new();

    public void Dispose()
    {
        foreach (var file in _created)
            if (File.Exists(file)) File.Delete(file);
    }

    private CoderAIController NewController(string? userId)
    {
        var claims = userId is null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        return new CoderAIController(scopeFactory: null!, _jobs, NullLogger<CoderAIController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
                },
            },
        };
    }

    private string WriteZip(string jobId)
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "Outputs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"coderai_{jobId}.zip");
        File.WriteAllBytes(path, new byte[] { 0x50, 0x4B });
        _created.Add(path);
        return path;
    }

    [Fact]
    public void The_controller_requires_authentication_itself()
    {
        // Kimlik doğrulaması AIQuotaMiddleware'in yan etkisine bırakılmıştı: middleware yol
        // önekine bakıyor, bir yeniden adlandırma kapıyı sessizce kaldırırdı.
        Assert.NotNull(typeof(CoderAIController).GetCustomAttribute<AuthorizeAttribute>(inherit: true));
    }

    [Theory]
    [InlineData("..%5C..%5C..%5Cappsettings.json")]
    [InlineData(@"..\..\..\appsettings.json")]
    [InlineData("../../appsettings.json")]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public void A_job_id_that_is_not_a_guid_never_reaches_the_filesystem(string jobId)
    {
        var result = NewController("user-1").DownloadZip(jobId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Another_users_zip_is_forbidden()
    {
        var jobId = Guid.NewGuid().ToString();
        _jobs.CreateJob(jobId, "owner");
        WriteZip(jobId);

        var result = NewController("intruder").DownloadZip(jobId);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public void The_owner_can_download_their_zip()
    {
        var jobId = Guid.NewGuid().ToString();
        _jobs.CreateJob(jobId, "owner");
        WriteZip(jobId);

        var result = NewController("owner").DownloadZip(jobId);

        var file = Assert.IsType<FileStreamResult>(result);
        file.FileStream.Dispose();
    }

    [Fact]
    public void An_unknown_job_is_not_found()
    {
        var result = NewController("owner").DownloadZip(Guid.NewGuid().ToString());

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
