using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.Scaffold;

namespace Namines.Infrastructure.Services;

/// <summary>
/// Tam yigin proje iskeleti uretir ve zip olarak dondurur.
///
/// <b>Yalnizca ORKESTRASYON:</b> hangi dosyanin hangi yola yazilacagina karar
/// verir; dosya iceriklerini <c>Generators/Scaffold/</c> altindaki hedef
/// basina ureteclerden alir (ARCH-003 / B-37). Once 1.760 satirlik tek
/// dosyaydi.
/// </summary>
public class ScaffolderService : IScaffolderService
{
    public Task<byte[]> GenerateFullStackProjectAsync(DatabaseSchema schema)
    {
        if (schema == null) throw new ArgumentNullException(nameof(schema));

        using (var memoryStream = new MemoryStream())
        {
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                // 1. Generate Domain Entities
                foreach (var table in schema.Tables)
                {
                    var entityContent = DotnetBackendScaffold.GenerateEntityCode(table, schema);
                    AddFileToZip(archive, $"backend/NaminesProject.Domain/Entities/{table.Name}.cs", entityContent);
                }

                // 2. Generate DbContext
                var dbContextContent = DotnetBackendScaffold.GenerateDbContextCode(schema);
                AddFileToZip(archive, "backend/NaminesProject.Infrastructure/Data/AppDbContext.cs", dbContextContent);

                // 3. Generate Controllers
                foreach (var table in schema.Tables)
                {
                    var controllerContent = DotnetBackendScaffold.GenerateControllerCode(table);
                    AddFileToZip(archive, $"backend/NaminesProject.API/Controllers/{table.Name}Controller.cs", controllerContent);
                }

                // 3.5 Generate BI Analytics Module
                if (schema.IncludeBiModule)
                {
                    AddFileToZip(archive, "backend/NaminesProject.API/Controllers/BiAnalyticsController.cs", BiModuleScaffold.GenerateBiControllerCode());
                    AddFileToZip(archive, "frontend-sdk/components/BiChatAssistant.tsx", BiModuleScaffold.GenerateBiComponentCode());
                }

                // 4. Generate Program.cs and configuration
                AddFileToZip(archive, "backend/NaminesProject.API/Program.cs", DotnetBackendScaffold.GenerateProgramCs());
                AddFileToZip(archive, "backend/NaminesProject.API/appsettings.json", DotnetBackendScaffold.GenerateAppSettings());
                AddFileToZip(archive, "backend/NaminesProject.API/Dockerfile", DotnetBackendScaffold.GenerateDockerfile());
                AddFileToZip(archive, "backend/NaminesProject.API/NaminesProject.API.csproj", DotnetBackendScaffold.GenerateCsproj());

                // 5. Generate Frontend SDK
                var frontendTypes = FrontendSdkScaffold.GenerateFrontendTypes(schema);
                AddFileToZip(archive, "frontend-sdk/types.ts", frontendTypes);

                var frontendZod = FrontendSdkScaffold.GenerateFrontendZod(schema);
                AddFileToZip(archive, "frontend-sdk/zodSchemas.ts", frontendZod);

                var frontendHooks = FrontendSdkScaffold.GenerateFrontendHooks(schema);
                AddFileToZip(archive, "frontend-sdk/queryHooks.ts", frontendHooks);

                // 6. Generate Docker Compose
                AddFileToZip(archive, "docker-compose.yml", DotnetBackendScaffold.GenerateDockerCompose());

                // 7. Cloud IaC & CI/CD Generation
                if (!string.IsNullOrEmpty(schema.CloudProvider) && !string.Equals(schema.CloudProvider, "None", StringComparison.OrdinalIgnoreCase))
                {
                    bool isAws = string.Equals(schema.CloudProvider, "AWS", StringComparison.OrdinalIgnoreCase);
                    AddFileToZip(archive, "infrastructure/terraform/main.tf", isAws ? CloudInfraScaffold.GenerateAwsTerraformMain() : CloudInfraScaffold.GenerateAzureTerraformMain());
                    AddFileToZip(archive, "infrastructure/terraform/variables.tf", isAws ? CloudInfraScaffold.GenerateAwsTerraformVariables() : CloudInfraScaffold.GenerateAzureTerraformVariables());
                    AddFileToZip(archive, "infrastructure/terraform/outputs.tf", isAws ? CloudInfraScaffold.GenerateAwsTerraformOutputs() : CloudInfraScaffold.GenerateAzureTerraformOutputs());
                    AddFileToZip(archive, ".github/workflows/deploy.yml", isAws ? CloudInfraScaffold.GenerateAwsGithubWorkflow() : CloudInfraScaffold.GenerateAzureGithubWorkflow());
                }
            }

            return Task.FromResult(memoryStream.ToArray());
        }
    }

    private void AddFileToZip(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
        {
            writer.Write(content);
        }
    }

    public Task<byte[]> GeneratePythonFreemiumProjectAsync(DatabaseSchema schema)
    {
        if (schema == null) throw new ArgumentNullException(nameof(schema));

        using (var memoryStream = new MemoryStream())
        {
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                AddFileToZip(archive, "database.py", PythonScaffold.GeneratePythonDatabasePy(schema));
                AddFileToZip(archive, "app.py", PythonScaffold.GeneratePythonAppPy(schema));
                AddFileToZip(archive, "requirements.txt", PythonScaffold.GeneratePythonRequirements());
                AddFileToZip(archive, "Dockerfile", PythonScaffold.GeneratePythonDockerfile());
                AddFileToZip(archive, "docker-compose.yml", PythonScaffold.GeneratePythonDockerCompose());
                AddFileToZip(archive, ".env.example", PythonScaffold.GeneratePythonEnvExample());
                AddFileToZip(archive, "README.md", PythonScaffold.GeneratePythonReadme(schema));
            }

            return Task.FromResult(memoryStream.ToArray());
        }
    }
}
