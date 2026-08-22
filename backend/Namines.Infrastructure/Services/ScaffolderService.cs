using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.Infrastructure.Services;

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
                    var entityContent = GenerateEntityCode(table, schema);
                    AddFileToZip(archive, $"backend/NaminesProject.Domain/Entities/{table.Name}.cs", entityContent);
                }

                // 2. Generate DbContext
                var dbContextContent = GenerateDbContextCode(schema);
                AddFileToZip(archive, "backend/NaminesProject.Infrastructure/Data/AppDbContext.cs", dbContextContent);

                // 3. Generate Controllers
                foreach (var table in schema.Tables)
                {
                    var controllerContent = GenerateControllerCode(table);
                    AddFileToZip(archive, $"backend/NaminesProject.API/Controllers/{table.Name}Controller.cs", controllerContent);
                }

                // 3.5 Generate BI Analytics Module
                if (schema.IncludeBiModule)
                {
                    AddFileToZip(archive, "backend/NaminesProject.API/Controllers/BiAnalyticsController.cs", GenerateBiControllerCode());
                    AddFileToZip(archive, "frontend-sdk/components/BiChatAssistant.tsx", GenerateBiComponentCode());
                }

                // 4. Generate Program.cs and configuration
                AddFileToZip(archive, "backend/NaminesProject.API/Program.cs", GenerateProgramCs());
                AddFileToZip(archive, "backend/NaminesProject.API/appsettings.json", GenerateAppSettings());
                AddFileToZip(archive, "backend/NaminesProject.API/Dockerfile", GenerateDockerfile());
                AddFileToZip(archive, "backend/NaminesProject.API/NaminesProject.API.csproj", GenerateCsproj());

                // 5. Generate Frontend SDK
                var frontendTypes = GenerateFrontendTypes(schema);
                AddFileToZip(archive, "frontend-sdk/types.ts", frontendTypes);

                var frontendZod = GenerateFrontendZod(schema);
                AddFileToZip(archive, "frontend-sdk/zodSchemas.ts", frontendZod);

                var frontendHooks = GenerateFrontendHooks(schema);
                AddFileToZip(archive, "frontend-sdk/queryHooks.ts", frontendHooks);

                // 6. Generate Docker Compose
                AddFileToZip(archive, "docker-compose.yml", GenerateDockerCompose());

                // 7. Cloud IaC & CI/CD Generation
                if (!string.IsNullOrEmpty(schema.CloudProvider) && !string.Equals(schema.CloudProvider, "None", StringComparison.OrdinalIgnoreCase))
                {
                    bool isAws = string.Equals(schema.CloudProvider, "AWS", StringComparison.OrdinalIgnoreCase);
                    AddFileToZip(archive, "infrastructure/terraform/main.tf", isAws ? GenerateAwsTerraformMain() : GenerateAzureTerraformMain());
                    AddFileToZip(archive, "infrastructure/terraform/variables.tf", isAws ? GenerateAwsTerraformVariables() : GenerateAzureTerraformVariables());
                    AddFileToZip(archive, "infrastructure/terraform/outputs.tf", isAws ? GenerateAwsTerraformOutputs() : GenerateAzureTerraformOutputs());
                    AddFileToZip(archive, ".github/workflows/deploy.yml", isAws ? GenerateAwsGithubWorkflow() : GenerateAzureGithubWorkflow());
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

    private static string Pluralize(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        if (word.EndsWith("y", StringComparison.OrdinalIgnoreCase))
        {
            bool isUpper = char.IsUpper(word[word.Length - 1]);
            string suffix = isUpper ? "IES" : "ies";
            return word.Substring(0, word.Length - 1) + suffix;
        }
        
        if (word.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("sh", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
            word.EndsWith("z", StringComparison.OrdinalIgnoreCase))
        {
            bool isUpper = char.IsUpper(word[word.Length - 1]);
            string suffix = isUpper ? "ES" : "es";
            return word + suffix;
        }

        bool isLastUpper = char.IsUpper(word[word.Length - 1]);
        string standardSuffix = isLastUpper ? "S" : "s";
        return word + standardSuffix;
    }

    private string GenerateEntityCode(SchemaTable table, DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine();
        sb.AppendLine("namespace NaminesProject.Domain.Entities;");
        sb.AppendLine();
        sb.AppendLine($"public class {table.Name}");
        sb.AppendLine("{");

        foreach (var col in table.Columns)
        {
            var csharpType = MapSqlToCsharpType(col.Type, col.IsNullable);
            sb.AppendLine($"    public {csharpType} {col.Name} {{ get; set; }}");
        }

        // Parent properties (Many-to-One / One-to-One)
        // Where this table is the SourceTable (holds the FK)
        var parentRelations = schema.Relations.Where(r => r.SourceTableId == table.Id).ToList();
        foreach (var rel in parentRelations)
        {
            var parentTable = schema.Tables.FirstOrDefault(t => t.Id == rel.TargetTableId);
            if (parentTable != null)
            {
                sb.AppendLine();
                sb.AppendLine("    [JsonIgnore]");
                sb.AppendLine($"    public virtual {parentTable.Name} {parentTable.Name} {{ get; set; }}");
            }
        }

        // Child collection properties (One-to-Many)
        // Where this table is the TargetTable (holds the PK)
        var childRelations = schema.Relations.Where(r => r.TargetTableId == table.Id).ToList();
        foreach (var rel in childRelations)
        {
            var childTable = schema.Tables.FirstOrDefault(t => t.Id == rel.SourceTableId);
            if (childTable != null)
            {
                sb.AppendLine();
                sb.AppendLine("    [JsonIgnore]");
                sb.AppendLine($"    public virtual ICollection<{childTable.Name}> {Pluralize(childTable.Name)} {{ get; set; }} = new List<{childTable.Name}>();");
            }
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private string GenerateDbContextCode(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using NaminesProject.Domain.Entities;");
        sb.AppendLine();
        sb.AppendLine("namespace NaminesProject.Infrastructure.Data;");
        sb.AppendLine();
        sb.AppendLine("public class AppDbContext : DbContext");
        sb.AppendLine("{");
        sb.AppendLine("    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)");
        sb.AppendLine("    {");
        sb.AppendLine("    }");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"    public DbSet<{table.Name}> {Pluralize(table.Name)} {{ get; set; }}");
        }

        sb.AppendLine();
        sb.AppendLine("    protected override void OnModelCreating(ModelBuilder modelBuilder)");
        sb.AppendLine("    {");
        sb.AppendLine("        base.OnModelCreating(modelBuilder);");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            var pkCol = table.Columns.FirstOrDefault(c => c.IsPK);
            if (pkCol != null)
            {
                sb.AppendLine($"        modelBuilder.Entity<{table.Name}>().HasKey(e => e.{pkCol.Name});");
            }
        }

        foreach (var rel in schema.Relations)
        {
            var sourceTable = schema.Tables.FirstOrDefault(t => t.Id == rel.SourceTableId);
            var targetTable = schema.Tables.FirstOrDefault(t => t.Id == rel.TargetTableId);
            if (sourceTable != null && targetTable != null)
            {
                var fkCol = sourceTable.Columns.FirstOrDefault(c => c.Id == rel.SourceColumnId);
                if (fkCol != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"        modelBuilder.Entity<{sourceTable.Name}>()");
                    sb.AppendLine($"            .HasOne(e => e.{targetTable.Name})");
                    sb.AppendLine($"            .WithMany(e => e.{Pluralize(sourceTable.Name)})");
                    sb.AppendLine($"            .HasForeignKey(e => e.{fkCol.Name});");
                }
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private string GenerateControllerCode(SchemaTable table)
    {
        var pkCol = table.Columns.FirstOrDefault(c => c.IsPK) ?? table.Columns.FirstOrDefault();
        var pkName = pkCol != null ? pkCol.Name : "Id";
        var pkType = pkCol != null ? MapSqlToCsharpType(pkCol.Type, false) : "int";

        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using NaminesProject.Infrastructure.Data;");
        sb.AppendLine("using NaminesProject.Domain.Entities;");
        sb.AppendLine();
        sb.AppendLine("namespace NaminesProject.API.Controllers;");
        sb.AppendLine();
        sb.AppendLine("[ApiController]");
        sb.AppendLine($"[Route(\"api/[controller]\")]");
        sb.AppendLine($"public class {table.Name}Controller : ControllerBase");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly AppDbContext _context;");
        sb.AppendLine();
        sb.AppendLine($"    public {table.Name}Controller(AppDbContext context)");
        sb.AppendLine("    {");
        sb.AppendLine("        _context = context;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [HttpGet]");
        sb.AppendLine($"    public async Task<ActionResult<IEnumerable<{table.Name}>>> GetAll()");
        sb.AppendLine("    {");
        sb.AppendLine($"        return await _context.{Pluralize(table.Name)}.ToListAsync();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [HttpGet(\"{id}\")]");
        sb.AppendLine($"    public async Task<ActionResult<{table.Name}>> GetById({pkType} id)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var entity = await _context.{Pluralize(table.Name)}.FindAsync(id);");
        sb.AppendLine("        if (entity == null) return NotFound();");
        sb.AppendLine("        return entity;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [HttpPost]");
        sb.AppendLine($"    public async Task<ActionResult<{table.Name}>> Create([FromBody] {table.Name} entity)");
        sb.AppendLine("    {");
        sb.AppendLine($"        _context.{Pluralize(table.Name)}.Add(entity);");
        sb.AppendLine("        await _context.SaveChangesAsync();");
        sb.AppendLine($"        return CreatedAtAction(nameof(GetById), new {{ id = entity.{pkName} }}, entity);");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [HttpPut(\"{id}\")]");
        sb.AppendLine($"    public async Task<IActionResult> Update({pkType} id, [FromBody] {table.Name} entity)");
        sb.AppendLine("    {");
        sb.AppendLine($"        if (id != entity.{pkName}) return BadRequest(\"ID mismatch\");");
        sb.AppendLine();
        sb.AppendLine("        _context.Entry(entity).State = EntityState.Modified;");
        sb.AppendLine();
        sb.AppendLine("        try");
        sb.AppendLine("        {");
        sb.AppendLine("            await _context.SaveChangesAsync();");
        sb.AppendLine("        }");
        sb.AppendLine("        catch (DbUpdateConcurrencyException)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (!await EntityExists(id)) return NotFound();");
        sb.AppendLine("            throw;");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        return NoContent();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [HttpDelete(\"{id}\")]");
        sb.AppendLine($"    public async Task<IActionResult> Delete({pkType} id)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var entity = await _context.{Pluralize(table.Name)}.FindAsync(id);");
        sb.AppendLine("        if (entity == null) return NotFound();");
        sb.AppendLine();
        sb.AppendLine($"        _context.{Pluralize(table.Name)}.Remove(entity);");
        sb.AppendLine("        await _context.SaveChangesAsync();");
        sb.AppendLine("        return NoContent();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    private Task<bool> EntityExists({pkType} id)");
        sb.AppendLine("    {");
        sb.AppendLine($"        return _context.{Pluralize(table.Name)}.AnyAsync(e => e.{pkName} == id);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private string GenerateFrontendTypes(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"export interface {table.Name} {{");
            foreach (var col in table.Columns)
            {
                var tsType = MapSqlToTsType(col.Type);
                var isOptional = col.IsNullable ? "?" : "";
                sb.AppendLine($"  {col.Name.ToLowerInvariant()}{isOptional}: {tsType};");
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string GenerateFrontendZod(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("import { z } from 'zod';");
        sb.AppendLine();
        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"export const {table.Name}Schema = z.object({{");
            foreach (var col in table.Columns)
            {
                var zodDef = MapSqlToZod(col.Type, col.IsNullable);
                sb.AppendLine($"  {col.Name.ToLowerInvariant()}: {zodDef},");
            }
            sb.AppendLine("});");
            sb.AppendLine();
            sb.AppendLine($"export type {table.Name}Input = z.infer<typeof {table.Name}Schema>;");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string GenerateFrontendHooks(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';");
        sb.AppendLine("import axios from 'axios';");
        sb.AppendLine("import * as T from './types';");
        sb.AppendLine();
        sb.AppendLine("const API_BASE = '/api';");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"// ─── {table.Name} Hooks ──────────────────────────────────────────");
            sb.AppendLine();
            
            // Get All
            sb.AppendLine($"export function useGet{table.Name}s() {{");
            sb.AppendLine("  return useQuery<T." + table.Name + "[]>({");
            sb.AppendLine($"    queryKey: ['{table.Name}s'],");
            sb.AppendLine($"    queryFn: async () => {{");
            sb.AppendLine($"      const res = await axios.get(`${{API_BASE}}/{table.Name}`);");
            sb.AppendLine("      return res.data;");
            sb.AppendLine("    }");
            sb.AppendLine("  });");
            sb.AppendLine("}");
            sb.AppendLine();

            // Get By Id
            sb.AppendLine($"export function useGet{table.Name}(id: any) {{");
            sb.AppendLine("  return useQuery<T." + table.Name + ">({");
            sb.AppendLine($"    queryKey: ['{table.Name}', id],");
            sb.AppendLine($"    queryFn: async () => {{");
            sb.AppendLine($"      const res = await axios.get(`${{API_BASE}}/{table.Name}/${{id}}`);");
            sb.AppendLine("      return res.data;");
            sb.AppendLine("    },");
            sb.AppendLine("    enabled: id !== undefined && id !== null");
            sb.AppendLine("  });");
            sb.AppendLine("}");
            sb.AppendLine();

            // Create
            sb.AppendLine($"export function useCreate{table.Name}() {{");
            sb.AppendLine("  const queryClient = useQueryClient();");
            sb.AppendLine("  return useMutation<T." + table.Name + ", Error, T." + table.Name + ">({");
            sb.AppendLine($"    mutationFn: async (data) => {{");
            sb.AppendLine($"      const res = await axios.post(`${{API_BASE}}/{table.Name}`, data);");
            sb.AppendLine("      return res.data;");
            sb.AppendLine("    },");
            sb.AppendLine("    onSuccess: () => {");
            sb.AppendLine($"      queryClient.invalidateQueries({{ queryKey: ['{table.Name}s'] }});");
            sb.AppendLine("    }");
            sb.AppendLine("  });");
            sb.AppendLine("}");
            sb.AppendLine();

            // Update
            sb.AppendLine($"export function useUpdate{table.Name}(id: any) {{");
            sb.AppendLine("  const queryClient = useQueryClient();");
            sb.AppendLine("  return useMutation<void, Error, T." + table.Name + ">({");
            sb.AppendLine($"    mutationFn: async (data) => {{");
            sb.AppendLine($"      await axios.put(`${{API_BASE}}/{table.Name}/${{id}}`, data);");
            sb.AppendLine("    },");
            sb.AppendLine("    onSuccess: () => {");
            sb.AppendLine($"      queryClient.invalidateQueries({{ queryKey: ['{table.Name}s'] }});");
            sb.AppendLine($"      queryClient.invalidateQueries({{ queryKey: ['{table.Name}', id] }});");
            sb.AppendLine("    }");
            sb.AppendLine("  });");
            sb.AppendLine("}");
            sb.AppendLine();

            // Delete
            sb.AppendLine($"export function useDelete{table.Name}() {{");
            sb.AppendLine("  const queryClient = useQueryClient();");
            sb.AppendLine("  return useMutation<void, Error, any>({");
            sb.AppendLine($"    mutationFn: async (id) => {{");
            sb.AppendLine($"      await axios.delete(`${{API_BASE}}/{table.Name}/${{id}}`);");
            sb.AppendLine("    },");
            sb.AppendLine("    onSuccess: () => {");
            sb.AppendLine($"      queryClient.invalidateQueries({{ queryKey: ['{table.Name}s'] }});");
            sb.AppendLine("    }");
            sb.AppendLine("  });");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string GenerateProgramCs()
    {
        return @"using Microsoft.EntityFrameworkCore;
using NaminesProject.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container with JSON IgnoreCycles configuration.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure EF Core SQLite database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString(""DefaultConnection"")));

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy(""AllowAll"", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Automatically run EF migrations
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    context.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment() || true)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(""AllowAll"");
app.UseAuthorization();
app.MapControllers();

app.Run();
";
    }

    private string GenerateAppSettings()
    {
        return @"{
  ""Logging"": {
    ""LogLevel"": {
      ""Default"": ""Information"",
      ""Microsoft.AspNetCore"": ""Warning""
    }
  },
  ""AllowedHosts"": ""*"",
  ""ConnectionStrings"": {
    ""DefaultConnection"": ""Data Source=naminesdb.db""
  }
}
";
    }

    private string GenerateDockerfile()
    {
        return @"FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore ""backend/NaminesProject.API/NaminesProject.API.csproj""
RUN dotnet publish ""backend/NaminesProject.API/NaminesProject.API.csproj"" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT [""dotnet"", ""NaminesProject.API.dll""]
";
    }

    private string GenerateCsproj()
    {
        return @"<Project Sdk=""Microsoft.NET.Sdk.Web"">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Microsoft.EntityFrameworkCore"" Version=""8.0.2"" />
    <PackageReference Include=""Microsoft.EntityFrameworkCore.Sqlite"" Version=""8.0.2"" />
    <PackageReference Include=""Microsoft.EntityFrameworkCore.Design"" Version=""8.0.2"">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include=""Swashbuckle.AspNetCore"" Version=""6.5.0"" />
  </ItemGroup>

</Project>
";
    }

    private string GenerateDockerCompose()
    {
        return @"version: '3.8'

services:
  api:
    build:
      context: ./backend
      dockerfile: NaminesProject.API/Dockerfile
    ports:
      - ""8080:80""
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
    volumes:
      - sqlite-data:/app
    restart: always

volumes:
  sqlite-data:
";
    }

    private static string MapSqlToCsharpType(string sqlType, bool isNullable)
    {
        var type = sqlType.ToUpperInvariant();
        string csharpType;

        if (type.Contains("INT") || type.Contains("INTEGER"))
            csharpType = "int";
        else if (type.Contains("VARCHAR") || type.Contains("TEXT") || type.Contains("CHAR"))
            csharpType = "string";
        else if (type.Contains("DATE") || type.Contains("TIME"))
            csharpType = "DateTime";
        else if (type.Contains("DECIMAL") || type.Contains("MONEY"))
            csharpType = "decimal";
        else if (type.Contains("FLOAT") || type.Contains("DOUBLE"))
            csharpType = "double";
        else if (type.Contains("BIT") || type.Contains("BOOL"))
            csharpType = "bool";
        else if (type.Contains("UUID") || type.Contains("UNIQUEIDENTIFIER"))
            csharpType = "Guid";
        else
            csharpType = "string";

        if (isNullable && csharpType != "string")
        {
            csharpType += "?";
        }

        return csharpType;
    }

    private string MapSqlToTsType(string sqlType)
    {
        var type = sqlType.ToUpperInvariant();
        if (type.Contains("INT") || type.Contains("INTEGER") || type.Contains("DECIMAL") || type.Contains("MONEY") || type.Contains("FLOAT") || type.Contains("DOUBLE"))
            return "number";
        if (type.Contains("BIT") || type.Contains("BOOL"))
            return "boolean";
        return "string";
    }

    private string MapSqlToZod(string sqlType, bool isNullable)
    {
        var tsType = MapSqlToTsType(sqlType);
        string zodDef;

        if (tsType == "number")
        {
            if (sqlType.ToUpperInvariant().Contains("INT"))
                zodDef = "z.number().int()";
            else
                zodDef = "z.number()";
        }
        else if (tsType == "boolean")
        {
            zodDef = "z.boolean()";
        }
        else
        {
            if (sqlType.ToUpperInvariant().Contains("UUID") || sqlType.ToUpperInvariant().Contains("UNIQUEIDENTIFIER"))
                zodDef = "z.string().uuid()";
            else
                zodDef = "z.string()";
        }

        if (isNullable)
        {
            zodDef += ".nullable().optional()";
        }

        return zodDef;
    }

    private string GenerateAwsTerraformMain()
    {
        return @"# Terraform config for AWS deployment
provider ""aws"" {
  region = var.aws_region
}

resource ""aws_vpc"" ""main"" {
  cidr_block           = ""10.0.0.0/16""
  enable_dns_hostnames = true
  tags = {
    Name = ""namines-vpc""
  }
}

resource ""aws_subnet"" ""public_a"" {
  vpc_id                  = aws_vpc.main.id
  cidr_block              = ""10.0.1.0/24""
  availability_zone       = ""${var.aws_region}a""
  map_public_ip_on_launch = true
}

resource ""aws_subnet"" ""public_b"" {
  vpc_id                  = aws_vpc.main.id
  cidr_block              = ""10.0.2.0/24""
  availability_zone       = ""${var.aws_region}b""
  map_public_ip_on_launch = true
}

resource ""aws_internet_gateway"" ""gw"" {
  vpc_id = aws_vpc.main.id
}

resource ""aws_route_table"" ""public"" {
  vpc_id = aws_vpc.main.id
  route {
    cidr_block = ""0.0.0.0/0""
    gateway_id = aws_internet_gateway.gw.id
  }
}

resource ""aws_route_table_association"" ""a"" {
  subnet_id      = aws_subnet.public_a.id
  route_table_id = aws_route_table.public.id
}

resource ""aws_route_table_association"" ""b"" {
  subnet_id      = aws_subnet.public_b.id
  route_table_id = aws_route_table.public.id
}

resource ""aws_security_group"" ""api_sg"" {
  name   = ""namines-api-sg""
  vpc_id = aws_vpc.main.id
  ingress {
    from_port   = 80
    to_port     = 80
    protocol    = ""tcp""
    cidr_blocks = [""0.0.0.0/0""]
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = ""-1""
    cidr_blocks = [""0.0.0.0/0""]
  }
}

resource ""aws_ecs_cluster"" ""main"" {
  name = ""namines-ecs-cluster""
}

resource ""aws_db_subnet_group"" ""db_subnets"" {
  name       = ""namines-db-subnet-group""
  subnet_ids = [aws_subnet.public_a.id, aws_subnet.public_b.id]
}

resource ""aws_security_group"" ""db_sg"" {
  name   = ""namines-db-sg""
  vpc_id = aws_vpc.main.id
  ingress {
    from_port       = 5432
    to_port         = 5432
    protocol        = ""tcp""
    security_groups = [aws_security_group.api_sg.id]
  }
}

resource ""aws_db_instance"" ""postgres"" {
  allocated_storage      = 20
  engine                 = ""postgres""
  engine_version         = ""15""
  instance_class         = ""db.t3.micro""
  db_name                = ""naminesdb""
  username               = ""dbadmin""
  password               = var.db_password
  db_subnet_group_name   = aws_db_subnet_group.db_subnets.name
  vpc_security_group_ids = [aws_security_group.db_sg.id]
  skip_final_snapshot    = true
}
";
    }

    private string GenerateAwsTerraformVariables()
    {
        return @"variable ""aws_region"" {
  type    = string
  default = ""eu-central-1""
}

variable ""db_password"" {
  type      = string
  sensitive = true
}
";
    }

    private string GenerateAwsTerraformOutputs()
    {
        return @"output ""db_endpoint"" {
  value = aws_db_instance.postgres.endpoint
}

output ""ecs_cluster_name"" {
  value = aws_ecs_cluster.main.name
}
";
    }

    private string GenerateAwsGithubWorkflow()
    {
        return @"name: Zero-to-Cloud CI/CD Deploy (AWS)

on:
  push:
    branches: [ ""main"" ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3

    - name: Configure AWS Credentials
      uses: aws-actions/configure-aws-credentials@v1
      with:
        aws-access-key-id: ${{ secrets.AWS_ACCESS_KEY_ID }}
        aws-secret-access-key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
        aws-region: eu-central-1

    - name: Docker Build and Push
      run: |
        docker build -t naminesproject-api:latest -f backend/NaminesProject.API/Dockerfile .
        # ecr push commands go here...

    - name: Setup Terraform
      uses: hashicorp/setup-terraform@v2

    - name: Terraform Apply
      run: |
        cd infrastructure/terraform
        terraform init
        terraform apply -auto-approve -var=""db_password=${{ secrets.DB_PASSWORD }}""
";
    }

    private string GenerateAzureTerraformMain()
    {
        return @"# Terraform config for Azure deployment
provider ""azurerm"" {
  features {}
}

resource ""azurerm_resource_group"" ""rg"" {
  name     = ""rg-namines-prod""
  location = var.location
}

resource ""azurerm_service_plan"" ""plan"" {
  name                = ""asp-namines""
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  os_type             = ""Linux""
  sku_name            = ""B1""
}

resource ""azurerm_linux_web_app"" ""app"" {
  name                = ""app-namines-api""
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  service_plan_id     = azurerm_service_plan.plan.id
  site_config {
    always_on = false
    application_stack {
      docker_image_name   = ""naminesproject-api:latest""
      docker_registry_url = ""https://index.docker.io/v1""
    }
  }
}

resource ""azurerm_mssql_server"" ""sqlserver"" {
  name                         = ""sql-namines-prod""
  resource_group_name          = azurerm_resource_group.rg.name
  location                     = azurerm_resource_group.rg.location
  version                      = ""12.0""
  administrator_login          = ""sqladmin""
  administrator_login_password = var.sql_admin_password
}

resource ""azurerm_mssql_database"" ""sqldb"" {
  name        = ""naminesdb""
  server_id   = azurerm_mssql_server.sqlserver.id
  collation   = ""SQL_Latin1_General_CP1_CI_AS""
  max_size_gb = 2
  sku_name    = ""Basic""
}
";
    }

    private string GenerateAzureTerraformVariables()
    {
        return @"variable ""location"" {
  type    = string
  default = ""westeurope""
}

variable ""sql_admin_password"" {
  type      = string
  sensitive = true
}
";
    }

    private string GenerateAzureTerraformOutputs()
    {
        return @"output ""app_url"" {
  value = azurerm_linux_web_app.app.default_hostname
}

output ""sql_server_fqdn"" {
  value = azurerm_mssql_server.sqlserver.fully_qualified_domain_name
}
";
    }

    private string GenerateAzureGithubWorkflow()
    {
        return @"name: Zero-to-Cloud CI/CD Deploy (Azure)

on:
  push:
    branches: [ ""main"" ]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3

    - name: Log in to Azure
      uses: azure/login@v1
      with:
        creds: ${{ secrets.AZURE_CREDENTIALS }}

    - name: Setup Terraform
      uses: hashicorp/setup-terraform@v2

    - name: Terraform Apply
      run: |
        cd infrastructure/terraform
        terraform init
        terraform apply -auto-approve -var=""sql_admin_password=${{ secrets.SQL_ADMIN_PASSWORD }}""
";
    }

    private string GenerateBiControllerCode()
    {
        return @"using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NaminesProject.Infrastructure.Data;

namespace NaminesProject.API.Controllers;

[ApiController]
[Route(""api/bi"")]
public class BiAnalyticsController : ControllerBase
{
    private readonly AppDbContext _context;

    public BiAnalyticsController(AppDbContext context)
    {
        _context = context;
    }

    public class AskRequest
    {
        public string Question { get; set; } = string.Empty;
    }

    [HttpPost(""ask"")]
    public async Task<IActionResult> Ask([FromBody] AskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { error = ""Question field cannot be empty"" });
        }

        string sql = TranslateToSql(request.Question);

        var sanitized = sql.Trim().TrimEnd(';');
        if (!Regex.IsMatch(sanitized, @""^\s*SELECT\s"", RegexOptions.IgnoreCase))
        {
            return BadRequest(new { error = ""For security reasons, only read-only SELECT queries can be executed."" });
        }

        var forbidden = new[] { ""INSERT"", ""UPDATE"", ""DELETE"", ""DROP"", ""ALTER"", ""CREATE"", 
                                ""TRUNCATE"", ""EXEC"", ""EXECUTE"", ""CALL"", ""GRANT"", ""REVOKE"", 
                                ""MERGE"", ""INTO"" };
        foreach (var kw in forbidden)
        {
            if (Regex.IsMatch(sanitized, $@""\b{kw}\b"", RegexOptions.IgnoreCase))
            {
                return BadRequest(new { error = ""Invalid SQL expression. Unsupported commands detected."" });
            }
        }

        if (sanitized.Contains(';'))
        {
            return BadRequest(new { error = ""Multiple statements are not allowed."" });
        }

        try
        {
            var data = new List<Dictionary<string, object>>();
            var connection = _context.Database.GetDbConnection();
            
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.GetValue(i);
                        }
                        data.Add(row);
                    }
                }
            }

            string chartType = RecommendChart(sql, data);
            var firstRow = data.FirstOrDefault();
            var xAxisKey = firstRow?.Keys.FirstOrDefault(k => k.ToLowerInvariant().Contains(""name"") || k.ToLowerInvariant().Contains(""date"") || k.ToLowerInvariant().Contains(""title"") || k.ToLowerInvariant().Contains(""id"")) ?? firstRow?.Keys.FirstOrDefault() ?? """";
            var yAxisKey = firstRow?.Keys.FirstOrDefault(k => !string.Equals(k, xAxisKey, StringComparison.OrdinalIgnoreCase) && (k.ToLowerInvariant().Contains(""count"") || k.ToLowerInvariant().Contains(""total"") || k.ToLowerInvariant().Contains(""price"") || k.ToLowerInvariant().Contains(""amount"") || k.ToLowerInvariant().Contains(""quantity"") || k.ToLowerInvariant().Contains(""id""))) ?? firstRow?.Keys.FirstOrDefault(k => !string.Equals(k, xAxisKey, StringComparison.OrdinalIgnoreCase)) ?? """";

            return Ok(new
            {
                question = request.Question,
                generatedSql = sql,
                chartType = chartType,
                xAxisKey = xAxisKey,
                yAxisKey = yAxisKey,
                data = data
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $""SQL Execution Error: {ex.Message}"", sql = sql });
        }
    }

    private string TranslateToSql(string question)
    {
        var q = question.ToLowerInvariant();
        var tables = _context.Model.GetEntityTypes().Select(t => t.GetTableName()).ToList();
        
        if (tables.Count == 0) return ""SELECT 1"";

        string primaryTable = tables[0];
        foreach (var t in tables)
        {
            if (q.Contains(t.ToLowerInvariant()))
            {
                primaryTable = t;
                break;
            }
        }

        if (q.Contains(""toplam"") || q.Contains(""total"") || q.Contains(""en çok"") || q.Contains(""most"") || q.Contains(""en yüksek"") || q.Contains(""highest""))
        {
            return $""SELECT {primaryTable}.*, COUNT(*) as Total FROM {primaryTable} GROUP BY 1 ORDER BY Total DESC LIMIT 10"";
        }
        
        if (q.Contains(""son"") || q.Contains(""latest"") || q.Contains(""tarih"") || q.Contains(""date"") || q.Contains(""yeni"") || q.Contains(""new""))
        {
            return $""SELECT * FROM {primaryTable} ORDER BY 1 DESC LIMIT 10"";
        }

        return $""SELECT * FROM {primaryTable} LIMIT 10"";
    }

    private string RecommendChart(string sql, List<Dictionary<string, object>> data)
    {
        if (data.Count == 0) return ""table"";
        return sql.ToUpperInvariant().Contains(""GROUP BY"") ? ""pie"" : ""bar"";
    }
}
";
    }

    private string GenerateBiComponentCode()
    {
        return @"import React, { useState } from 'react';
import { Sparkles, Send, Database, BarChart2, PieChart as PieIcon, LineChart as LineIcon, Table } from 'lucide-react';
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, PieChart, Pie, Cell, LineChart, Line } from 'recharts';

const COLORS = ['#6366f1', '#10b981', '#f59e0b', '#ec4899', '#8b5cf6', '#3b82f6'];

export default function BiChatAssistant() {
  const [question, setQuestion] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [messages, setMessages] = useState<any[]>([]);
  const [expandedSql, setExpandedSql] = useState<Record<number, boolean>>({});

  const handleAsk = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!question.trim()) return;

    const userMsg = { id: Date.now(), sender: 'user', text: question };
    setMessages(prev => [...prev, userMsg]);
    setQuestion('');
    setIsLoading(true);

    try {
      const response = await fetch('/api/bi/ask', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question: question })
      });
      
      const result = await response.json();
      
      if (response.ok) {
        setMessages(prev => [...prev, {
          id: Date.now() + 1,
          sender: 'assistant',
          sql: result.generatedSql,
          chartType: result.chartType,
          xAxisKey: result.xAxisKey,
          yAxisKey: result.yAxisKey,
          data: result.data
        }]);
      } else {
        setMessages(prev => [...prev, {
          id: Date.now() + 1,
          sender: 'assistant',
          error: result.error || 'An error occurred during analysis.'
        }]);
      }
    } catch (err) {
      setMessages(prev => [...prev, {
        id: Date.now() + 1,
        sender: 'assistant',
        error: 'API connection failed.'
      }]);
    } finally {
      setIsLoading(false);
    }
  };

  const renderChart = (msg: any) => {
    if (!msg.data || msg.data.length === 0) return <div className=""text-zinc-500 text-xs py-4 text-center"">No data to display.</div>;

    const xAxis = msg.xAxisKey;
    const yAxis = msg.yAxisKey;

    switch (msg.chartType) {
      case 'bar':
        return (
          <ResponsiveContainer width=""100%"" height={220}>
            <BarChart data={msg.data}>
              <CartesianGrid strokeDasharray=""3 3"" stroke=""#27272a"" />
              <XAxis dataKey={xAxis} stroke=""#a1a1aa"" fontSize={10} />
              <YAxis stroke=""#a1a1aa"" fontSize={10} />
              <Tooltip contentStyle={{ backgroundColor: '#09090b', borderColor: '#27272a', borderRadius: '8px' }} />
              <Bar dataKey={yAxis} fill=""#6366f1"" radius={[4, 4, 0, 0]}>
                {msg.data.map((entry: any, index: number) => (
                  <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        );
      case 'pie':
        return (
          <ResponsiveContainer width=""100%"" height={220}>
            <PieChart>
              <Pie data={msg.data} dataKey={yAxis} nameKey={xAxis} cx=""50%"" cy=""50%"" outerRadius={70} fill=""#6366f1"" label={{ fontSize: 9, fill: '#e4e4e7' }}>
                {msg.data.map((entry: any, index: number) => (
                  <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                ))}
              </Pie>
              <Tooltip contentStyle={{ backgroundColor: '#09090b', borderColor: '#27272a', borderRadius: '8px' }} />
            </PieChart>
          </ResponsiveContainer>
        );
      case 'line':
        return (
          <ResponsiveContainer width=""100%"" height={220}>
            <LineChart data={msg.data}>
              <CartesianGrid strokeDasharray=""3 3"" stroke=""#27272a"" />
              <XAxis dataKey={xAxis} stroke=""#a1a1aa"" fontSize={10} />
              <YAxis stroke=""#a1a1aa"" fontSize={10} />
              <Tooltip contentStyle={{ backgroundColor: '#09090b', borderColor: '#27272a', borderRadius: '8px' }} />
              <Line type=""monotone"" dataKey={yAxis} stroke=""#10b981"" strokeWidth={2} activeDot={{ r: 6 }} />
            </LineChart>
          </ResponsiveContainer>
        );
      default:
        return (
          <div className=""overflow-x-auto max-h-48 border border-zinc-800 rounded-xl"">
            <table className=""min-w-full divide-y divide-zinc-800 text-left text-xs text-zinc-300"">
              <thead className=""bg-zinc-900/60 text-zinc-400 font-semibold"">
                <tr>
                  {Object.keys(msg.data[0] || {}).map(key => (
                    <th key={key} className=""px-4 py-2 border-b border-zinc-800"">{key}</th>
                  ))}
                </tr>
              </thead>
              <tbody className=""divide-y divide-zinc-800/50 bg-zinc-950/20"">
                {msg.data.map((row: any, i: number) => (
                  <tr key={i} className=""hover:bg-zinc-900/20"">
                    {Object.values(row).map((val: any, j: number) => (
                      <td key={j} className=""px-4 py-1.5 whitespace-nowrap"">{String(val)}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        );
    }
  };

  return (
    <div className=""flex flex-col bg-zinc-950 border border-zinc-800 rounded-2xl w-full max-w-lg h-[500px] shadow-2xl overflow-hidden text-zinc-100 font-sans"">
      <div className=""bg-gradient-to-r from-indigo-950/50 to-zinc-900/80 border-b border-zinc-800 px-4 py-3 flex items-center justify-between"">
        <div className=""flex items-center gap-2"">
          <Sparkles className=""w-5 h-5 text-indigo-400 animate-pulse"" />
          <div>
            <h3 className=""text-sm font-bold tracking-wide"">AI-BI Data Analytics</h3>
            <p className=""text-[10px] text-zinc-400"">Ask in natural language, see in charts.</p>
          </div>
        </div>
      </div>

      <div className=""flex-1 overflow-y-auto p-4 space-y-4"">
        {messages.length === 0 && (
          <div className=""h-full flex flex-col items-center justify-center text-center p-6 space-y-2"">
            <Database className=""w-10 h-10 text-zinc-700 animate-bounce"" />
            <p className=""text-xs text-zinc-400"">Try asking: ""Show total order count"" or ""List newest members""</p>
          </div>
        )}

        {messages.map(msg => (
          <div key={msg.id} className={`flex flex-col ${msg.sender === 'user' ? 'items-end' : 'items-start'}`}>
            <div className={`max-w-[85%] rounded-2xl px-4 py-2.5 text-xs ${
              msg.sender === 'user' 
                ? 'bg-indigo-600 text-white rounded-tr-none' 
                : 'bg-zinc-900 border border-zinc-800 text-zinc-200 rounded-tl-none space-y-3 w-full'
            }`}>
              {msg.sender === 'user' ? (
                msg.text
              ) : msg.error ? (
                <div className=""text-rose-400 font-medium"">⚠️ {msg.error}</div>
              ) : (
                <>
                  <div className=""bg-zinc-950 rounded-xl border border-zinc-800/80 overflow-hidden"">
                    <button
                      type=""button""
                      onClick={() => setExpandedSql(prev => ({ ...prev, [msg.id]: !prev[msg.id] }))}
                      className=""w-full px-3 py-2 flex items-center justify-between text-[10px] font-bold text-zinc-400 hover:text-white hover:bg-zinc-900/50 transition-all""
                    >
                      <span className=""flex items-center gap-1.5"">
                        <Database className=""w-3.5 h-3.5 text-indigo-400"" />
                        View Generated SQL
                      </span>
                      <span className=""text-[9px]"">
                        {expandedSql[msg.id] ? 'Hide' : 'Show'}
                      </span>
                    </button>
                    {expandedSql[msg.id] && (
                      <div className=""p-2.5 bg-zinc-900 border-t border-zinc-800/80 font-mono text-[10px] text-indigo-300 overflow-x-auto"">
                        {msg.sql}
                      </div>
                    )}
                  </div>
                  
                  <div className=""bg-zinc-950/45 p-2 rounded-xl border border-zinc-800/40"">
                    <div className=""flex justify-between items-center mb-2 px-1"">
                      <span className=""text-[9px] font-bold text-zinc-400 uppercase tracking-wide flex items-center gap-1"">
                        {msg.chartType === 'bar' && <BarChart2 className=""w-3.5 h-3.5 text-indigo-400"" />}
                        {msg.chartType === 'pie' && <PieIcon className=""w-3.5 h-3.5 text-emerald-400"" />}
                        {msg.chartType === 'line' && <LineIcon className=""w-3.5 h-3.5 text-sky-400"" />}
                        {msg.chartType === 'table' && <Table className=""w-3.5 h-3.5 text-amber-400"" />}
                        Visual Report ({msg.chartType})
                      </span>
                    </div>
                    {renderChart(msg)}
                  </div>
                </>
              )}
            </div>
          </div>
        ))}
        {isLoading && (
          <div className=""flex items-center gap-2 text-zinc-400 text-xs"">
            <Sparkles className=""w-4 h-4 text-indigo-400 animate-spin"" />
            <span>AI is analyzing data...</span>
          </div>
        )}
      </div>

      <form onSubmit={handleAsk} className=""border-t border-zinc-800/60 p-3 bg-zinc-900/45 flex gap-2"">
        </button>
      </form>
    </div>
  );
}
";
    }

    public Task<byte[]> GeneratePythonFreemiumProjectAsync(DatabaseSchema schema)
    {
        if (schema == null) throw new ArgumentNullException(nameof(schema));

        using (var memoryStream = new MemoryStream())
        {
            using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                AddFileToZip(archive, "database.py", GeneratePythonDatabasePy(schema));
                AddFileToZip(archive, "app.py", GeneratePythonAppPy(schema));
                AddFileToZip(archive, "requirements.txt", GeneratePythonRequirements());
                AddFileToZip(archive, "Dockerfile", GeneratePythonDockerfile());
                AddFileToZip(archive, "docker-compose.yml", GeneratePythonDockerCompose());
                AddFileToZip(archive, ".env.example", GeneratePythonEnvExample());
                AddFileToZip(archive, "README.md", GeneratePythonReadme(schema));
            }

            return Task.FromResult(memoryStream.ToArray());
        }
    }

    private string MapSqlToSqlAlchemyType(string type)
    {
        var t = type.ToUpperInvariant();
        if (t.Contains("INT")) return "Integer";
        if (t.Contains("VARCHAR") || t.Contains("TEXT") || t.Contains("CHAR") || t.Contains("STRING")) return "String";
        if (t.Contains("DECIMAL") || t.Contains("NUMERIC") || t.Contains("MONEY") || t.Contains("PRICE")) return "Numeric";
        if (t.Contains("DOUBLE") || t.Contains("FLOAT") || t.Contains("REAL")) return "Float";
        if (t.Contains("DATE") || t.Contains("TIME")) return "DateTime";
        if (t.Contains("BIT") || t.Contains("BOOL")) return "Boolean";
        return "String";
    }

    private string GeneratePythonDatabasePy(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("from sqlalchemy import create_engine, Column, Integer, String, Float, DateTime, Boolean, ForeignKey, Numeric");
        sb.AppendLine("from sqlalchemy.ext.declarative import declarative_base");
        sb.AppendLine("from sqlalchemy.orm import sessionmaker, relationship");
        sb.AppendLine("import os");
        sb.AppendLine("from datetime import datetime");
        sb.AppendLine();
        sb.AppendLine("DATABASE_URL = os.getenv('DATABASE_URL', 'postgresql://postgres:postgres@db:5432/postgres')");
        sb.AppendLine("engine = create_engine(DATABASE_URL)");
        sb.AppendLine("SessionLocal = sessionmaker(autocommit=False, autoflush=False, bind=engine)");
        sb.AppendLine("Base = declarative_base()");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            sb.AppendLine($"class {table.Name}(Base):");
            sb.AppendLine($"    __tablename__ = '{table.Name.ToLowerInvariant()}'");
            sb.AppendLine();

            foreach (var col in table.Columns)
            {
                var saType = MapSqlToSqlAlchemyType(col.Type);
                var parts = new List<string> { saType };

                if (col.IsPK) parts.Add("primary_key=True");
                if (col.IsNullable) parts.Add("nullable=True");
                else if (!col.IsPK) parts.Add("nullable=False");

                var rel = schema.Relations.FirstOrDefault(r => r.SourceTableId == table.Id && r.SourceColumnId == col.Id);
                if (rel != null)
                {
                    var targetTable = schema.Tables.FirstOrDefault(t => t.Id == rel.TargetTableId);
                    var targetCol = targetTable?.Columns.FirstOrDefault(c => c.Id == rel.TargetColumnId);
                    if (targetTable != null && targetCol != null)
                    {
                        parts.Add($"ForeignKey('{targetTable.Name.ToLowerInvariant()}.{targetCol.Name.ToLowerInvariant()}')");
                    }
                }

                sb.AppendLine($"    {col.Name.ToLowerInvariant()} = Column({string.Join(", ", parts)})");
            }

            var tableRelations = schema.Relations.Where(r => r.SourceTableId == table.Id).ToList();
            foreach (var rel in tableRelations)
            {
                var targetTable = schema.Tables.FirstOrDefault(t => t.Id == rel.TargetTableId);
                if (targetTable != null)
                {
                    sb.AppendLine($"    {targetTable.Name.ToLowerInvariant()} = relationship('{targetTable.Name}')");
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine("def init_db():");
        sb.AppendLine("    Base.metadata.create_all(bind=engine)");
        sb.AppendLine();

        return sb.ToString();
    }

    private string GeneratePythonAppPy(DatabaseSchema schema)
    {
        var sb = new StringBuilder();
        sb.AppendLine("import streamlit as st");
        sb.AppendLine("import pandas as pd");
        sb.AppendLine("from database import SessionLocal, init_db, " + string.Join(", ", schema.Tables.Select(t => t.Name)));
        sb.AppendLine("from sqlalchemy.inspection import inspect");
        sb.AppendLine("import datetime");
        sb.AppendLine();
        sb.AppendLine("st.set_page_config(page_title='Namines CRUD Admin', page_icon='🗂️', layout='wide', initial_sidebar_state='expanded')");
        sb.AppendLine();
        sb.AppendLine("st.markdown(\"\"\"");
        sb.AppendLine("    <style>");
        sb.AppendLine("    .main { background-color: #0b0f19; color: #f3f4f6; }");
        sb.AppendLine("    .stButton>button { background-color: #4f46e5; color: white; border-radius: 8px; font-weight: bold; }");
        sb.AppendLine("    </style>");
        sb.AppendLine("\"\"\", unsafe_allow_html=True)");
        sb.AppendLine();
        sb.AppendLine("try:");
        sb.AppendLine("    init_db()");
        sb.AppendLine("except Exception as e:");
        sb.AppendLine("    st.error(f'Database Connection Error: {e}')");
        sb.AppendLine();
        sb.AppendLine("db = SessionLocal()");
        sb.AppendLine();
        sb.AppendLine("st.sidebar.title('🗂️ CRUD Engine')");
        sb.AppendLine("st.sidebar.subheader('Namines Admin Panel')");
        sb.AppendLine();

        sb.AppendLine("nav_options = ['🏠 Dashboard'] + [" + string.Join(", ", schema.Tables.Select(t => $"'{t.Name}'")) + "]");
        sb.AppendLine("choice = st.sidebar.selectbox('Navigate', nav_options)");
        sb.AppendLine();

        sb.AppendLine("if choice == '🏠 Dashboard':");
        sb.AppendLine("    st.title('🏠 Live Dashboard')");
        sb.AppendLine("    st.write('System metrics and overview of the database.')");
        sb.AppendLine();
        sb.AppendLine("    cols = st.columns(3)");
        sb.AppendLine($"    cols[0].metric('Total Tables', {schema.Tables.Count})");
        sb.AppendLine("    ");
        sb.AppendLine("    table_data = []");
        foreach (var t in schema.Tables)
        {
            sb.AppendLine($"    try:");
            sb.AppendLine($"        count = db.query({t.Name}).count()");
            sb.AppendLine($"        table_data.append({{'Table': '{t.Name}', 'Row Count': count}})");
            sb.AppendLine($"    except Exception:");
            sb.AppendLine($"        table_data.append({{'Table': '{t.Name}', 'Row Count': 'Error'}})");
        }
        sb.AppendLine("    df_stats = pd.DataFrame(table_data)");
        sb.AppendLine("    cols[1].metric('Total Records', df_stats['Row Count'].sum() if 'Error' not in df_stats['Row Count'].values else 'Unknown')");
        sb.AppendLine();
        sb.AppendLine("    st.subheader('Database Schema Overview')");
        sb.AppendLine("    st.dataframe(df_stats, use_container_width=True)");
        sb.AppendLine();

        foreach (var table in schema.Tables)
        {
            var pkCol = table.Columns.FirstOrDefault(c => c.IsPK) ?? table.Columns.FirstOrDefault();
            var pkName = pkCol?.Name.ToLowerInvariant() ?? "id";

            sb.AppendLine($"elif choice == '{table.Name}':");
            sb.AppendLine($"    st.title('📋 {table.Name} Manager')");
            sb.AppendLine();
            sb.AppendLine("    action = st.radio('Select Action', ['View Records', 'Create Record', 'Update Record', 'Delete Record'], horizontal=True)");
            sb.AppendLine();
            
            sb.AppendLine("    if action == 'View Records':");
            sb.AppendLine($"        st.subheader('Registered Data')");
            sb.AppendLine($"        try:");
            sb.AppendLine($"            records = db.query({table.Name}).all()");
            sb.AppendLine($"            if records:");
            sb.AppendLine($"                data = []");
            sb.AppendLine($"                for r in records:");
            sb.AppendLine($"                    row = {{}}");
            foreach (var col in table.Columns)
            {
                sb.AppendLine($"                    row['{col.Name}'] = getattr(r, '{col.Name.ToLowerInvariant()}')");
            }
            sb.AppendLine($"                    data.append(row)");
            sb.AppendLine($"                st.dataframe(pd.DataFrame(data), use_container_width=True)");
            sb.AppendLine($"            else:");
            sb.AppendLine($"                st.info('No records found in this table.')");
            sb.AppendLine($"        except Exception as e:");
            sb.AppendLine($"            st.error(f'Error reading table: {{e}}')");
            sb.AppendLine();

            sb.AppendLine("    elif action == 'Create Record':");
            sb.AppendLine($"        st.subheader('Add New Record')");
            sb.AppendLine($"        with st.form('create_form_{table.Name}'):");

            foreach (var col in table.Columns)
            {
                if (col.IsPK)
                {
                    var saType = MapSqlToSqlAlchemyType(col.Type);
                    if (saType == "Integer")
                    {
                        sb.AppendLine($"            st.text_input('{col.Name}', value='Auto-Incremented', disabled=True)");
                        continue;
                    }
                }
                
                var saTypeForm = MapSqlToSqlAlchemyType(col.Type);
                if (saTypeForm == "Integer")
                {
                    sb.AppendLine($"            val_{col.Name.ToLowerInvariant()} = st.number_input('{col.Name}', value=0, step=1)");
                }
                else if (saTypeForm == "Numeric" || saTypeForm == "Float")
                {
                    sb.AppendLine($"            val_{col.Name.ToLowerInvariant()} = st.number_input('{col.Name}', value=0.0)");
                }
                else if (saTypeForm == "Boolean")
                {
                    sb.AppendLine($"            val_{col.Name.ToLowerInvariant()} = st.checkbox('{col.Name}', value=False)");
                }
                else if (saTypeForm == "DateTime")
                {
                    sb.AppendLine($"            val_{col.Name.ToLowerInvariant()} = st.date_input('{col.Name}', value=datetime.date.today())");
                }
                else
                {
                    sb.AppendLine($"            val_{col.Name.ToLowerInvariant()} = st.text_input('{col.Name}', value='')");
                }
            }
            sb.AppendLine("            submitted = st.form_submit_button('Save Record')");
            sb.AppendLine("            if submitted:");
            sb.AppendLine($"                try:");
            sb.AppendLine($"                    new_record = {table.Name}(");
            foreach (var col in table.Columns)
            {
                if (col.IsPK && MapSqlToSqlAlchemyType(col.Type) == "Integer") continue;
                var saTypeForm = MapSqlToSqlAlchemyType(col.Type);
                if (saTypeForm == "DateTime")
                {
                    sb.AppendLine($"                        {col.Name.ToLowerInvariant()}=datetime.datetime.combine(val_{col.Name.ToLowerInvariant()}, datetime.time.min),");
                }
                else
                {
                    sb.AppendLine($"                        {col.Name.ToLowerInvariant()}=val_{col.Name.ToLowerInvariant()},");
                }
            }
            sb.AppendLine($"                    )");
            sb.AppendLine($"                    db.add(new_record)");
            sb.AppendLine($"                    db.commit()");
            sb.AppendLine($"                    st.success('Record successfully added!')");
            sb.AppendLine($"                except Exception as e:");
            sb.AppendLine($"                    db.rollback()");
            sb.AppendLine($"                    st.error(f'Error saving record: {{e}}')");
            sb.AppendLine();

            sb.AppendLine("    elif action == 'Update Record':");
            sb.AppendLine($"        st.subheader('Modify Existing Record')");
            sb.AppendLine($"        try:");
            sb.AppendLine($"            records = db.query({table.Name}).all()");
            sb.AppendLine($"            if not records:");
            sb.AppendLine($"                st.info('No records to update.')");
            sb.AppendLine($"            else:");
            sb.AppendLine($"                keys = [getattr(r, '{pkName}') for r in records]");
            sb.AppendLine($"                selected_key = st.selectbox('Select Record to Update ({pkCol?.Name})', keys)");
            sb.AppendLine($"                record_to_update = db.query({table.Name}).filter(getattr({table.Name}, '{pkName}') == selected_key).first()");
            sb.AppendLine($"                if record_to_update:");
            sb.AppendLine($"                    with st.form('update_form_{table.Name}'):");
            
            foreach (var col in table.Columns)
            {
                if (col.IsPK)
                {
                    sb.AppendLine($"                        st.text_input('{col.Name}', value=str(getattr(record_to_update, '{col.Name.ToLowerInvariant()}')), disabled=True)");
                    continue;
                }
                var saTypeForm = MapSqlToSqlAlchemyType(col.Type);
                if (saTypeForm == "Integer")
                {
                    sb.AppendLine($"                        val_{col.Name.ToLowerInvariant()} = st.number_input('{col.Name}', value=int(getattr(record_to_update, '{col.Name.ToLowerInvariant()}') or 0), step=1)");
                }
                else if (saTypeForm == "Numeric" || saTypeForm == "Float")
                {
                    sb.AppendLine($"                        val_{col.Name.ToLowerInvariant()} = st.number_input('{col.Name}', value=float(getattr(record_to_update, '{col.Name.ToLowerInvariant()}') or 0.0))");
                }
                else if (saTypeForm == "Boolean")
                {
                    sb.AppendLine($"                        val_{col.Name.ToLowerInvariant()} = st.checkbox('{col.Name}', value=bool(getattr(record_to_update, '{col.Name.ToLowerInvariant()}') or False))");
                }
                else if (saTypeForm == "DateTime")
                {
                    sb.AppendLine($"                        orig_val = getattr(record_to_update, '{col.Name.ToLowerInvariant()}')");
                    sb.AppendLine($"                        val_{col.Name.ToLowerInvariant()} = st.date_input('{col.Name}', value=orig_val.date() if orig_val else datetime.date.today())");
                }
                else
                {
                    sb.AppendLine($"                        val_{col.Name.ToLowerInvariant()} = st.text_input('{col.Name}', value=str(getattr(record_to_update, '{col.Name.ToLowerInvariant()}') or ''))");
                }
            }
            sb.AppendLine("                        submitted = st.form_submit_button('Update')");
            sb.AppendLine("                        if submitted:");
            sb.AppendLine($"                            try:");
            foreach (var col in table.Columns)
            {
                if (col.IsPK) continue;
                var saTypeForm = MapSqlToSqlAlchemyType(col.Type);
                if (saTypeForm == "DateTime")
                {
                    sb.AppendLine($"                                record_to_update.{col.Name.ToLowerInvariant()} = datetime.datetime.combine(val_{col.Name.ToLowerInvariant()}, datetime.time.min)");
                }
                else
                {
                    sb.AppendLine($"                                record_to_update.{col.Name.ToLowerInvariant()} = val_{col.Name.ToLowerInvariant()}");
                }
            }
            sb.AppendLine($"                                db.commit()");
            sb.AppendLine($"                                st.success('Record successfully updated!')");
            sb.AppendLine($"                            except Exception as e:");
            sb.AppendLine($"                                db.rollback()");
            sb.AppendLine($"                                st.error(f'Error updating record: {{e}}')");
            sb.AppendLine($"        except Exception as e:");
            sb.AppendLine($"            st.error(f'System error: {{e}}')");
            sb.AppendLine();

            sb.AppendLine("    elif action == 'Delete Record':");
            sb.AppendLine($"        st.subheader('Remove Record')");
            sb.AppendLine($"        try:");
            sb.AppendLine($"            records = db.query({table.Name}).all()");
            sb.AppendLine($"            if not records:");
            sb.AppendLine($"                st.info('No records to delete.')");
            sb.AppendLine($"            else:");
            sb.AppendLine($"                keys = [getattr(r, '{pkName}') for r in records]");
            sb.AppendLine($"                selected_key = st.selectbox('Select Record to Delete ({pkCol?.Name})', keys)");
            sb.AppendLine($"                if st.button('Confirm Delete Record'):");
            sb.AppendLine($"                    try:");
            sb.AppendLine($"                        record_to_del = db.query({table.Name}).filter(getattr({table.Name}, '{pkName}') == selected_key).first()");
            sb.AppendLine($"                        if record_to_del:");
            sb.AppendLine($"                            db.delete(record_to_del)");
            sb.AppendLine($"                            db.commit()");
            sb.AppendLine($"                            st.success('Record deleted successfully!')");
            sb.AppendLine($"                            st.rerun()");
            sb.AppendLine($"                    except Exception as e:");
            sb.AppendLine($"                        db.rollback()");
            sb.AppendLine($"                        st.error(f'Error deleting record: {{e}}')");
            sb.AppendLine($"        except Exception as e:");
            sb.AppendLine($"            st.error(f'System error: {{e}}')");
            sb.AppendLine();
        }

        sb.AppendLine("db.close()");
        return sb.ToString();
    }

    private string GeneratePythonRequirements()
    {
        return @"streamlit==1.32.0
SQLAlchemy==2.0.28
psycopg2-binary==2.9.9
pandas==2.2.1
";
    }

    private string GeneratePythonDockerfile()
    {
        return @"FROM python:3.11-slim

WORKDIR /app

RUN apt-get update && apt-get install -y \
    build-essential \
    libpq-dev \
    curl \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

EXPOSE 8501

HEALTHCHECK CMD curl --fail http://localhost:8501/_stcore/health

ENTRYPOINT [""streamlit"", ""run"", ""app.py"", ""--server.port=8501"", ""--server.address=0.0.0.0""]
";
    }

    private string GeneratePythonDockerCompose()
    {
        return @"version: '3.8'

services:
  db:
    image: postgres:15-alpine
    container_name: namines_postgres_db
    restart: always
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: postgres
    ports:
      - ""5432:5432""
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: [""CMD-SHELL"", ""pg_isready -U postgres""]
      interval: 5s
      timeout: 5s
      retries: 5

  app:
    build: .
    container_name: namines_streamlit_app
    restart: always
    ports:
      - ""8501:8501""
    environment:
      - DATABASE_URL=postgresql://postgres:postgres@db:5432/postgres
    depends_on:
      db:
        condition: service_healthy

volumes:
  pgdata:
";
    }

    private string GeneratePythonEnvExample()
    {
        return @"DATABASE_URL=postgresql://postgres:postgres@localhost:5432/postgres
";
    }

    private string GeneratePythonReadme(DatabaseSchema schema)
    {
        return $@"# {schema.Name} - Python Streamlit CRUD Admin

This project is a freemium admin interface generated automatically by Namines for your database schema.

## Features
- **SQLAlchemy ORM** database mapping with relationship setup
- **Streamlit-based live CRUD** (Create, Read, Update, Delete) forms
- **Visual metrics dashboard** to see record count distribution
- **Dockerized development environment** using PostgreSQL

## Quick Start (with Docker)

1. Ensure Docker and Docker Compose are installed on your machine.
2. Build and run the services:
   ```bash
   docker compose up --build -d
   ```
3. Access your live Streamlit CRUD interface at:
   [http://localhost:8501](http://localhost:8501)

## Manual Run (Virtual Environment)

1. Create a virtual environment:
   ```bash
   python -m venv venv
   source venv/bin/activate  # On Windows: venv\Scripts\activate
   ```
2. Install dependencies:
   ```bash
   pip install -r requirements.txt
   ```
3. Set your PostgreSQL environment variable and run the application:
   ```bash
   export DATABASE_URL=postgresql://postgres:postgres@localhost:5432/postgres
   streamlit run app.py
   ```
";
    }
}
