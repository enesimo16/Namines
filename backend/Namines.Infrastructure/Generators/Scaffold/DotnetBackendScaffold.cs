using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.Scaffold;

/// <summary>
/// .NET backend projesi: entity, DbContext, controller, Program.cs, csproj, Docker.
///
/// <see cref="Namines.Infrastructure.Services.ScaffolderService"/>'ten ayrildi
/// (ARCH-003 / B-37): 1.760 satirlik tek dosya, hedef basina bir uretece
/// bolundu -- <c>Generators/Eject/</c> altindaki mevcut desenle ayni.
/// Metot govdeleri DEGISMEDI; <c>ScaffolderSnapshotTests</c> ciktinin bayt
/// bayt ayni kaldigini kanitliyor.
/// </summary>
internal static class DotnetBackendScaffold
{
    internal static string Pluralize(string word)
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

    internal static string GenerateEntityCode(SchemaTable table, DatabaseSchema schema)
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

    internal static string GenerateDbContextCode(DatabaseSchema schema)
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

    internal static string GenerateControllerCode(SchemaTable table)
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

    internal static string GenerateProgramCs()
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

    internal static string GenerateAppSettings()
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

    internal static string GenerateDockerfile()
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

    internal static string GenerateCsproj()
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

    internal static string GenerateDockerCompose()
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

    internal static string MapSqlToCsharpType(string sqlType, bool isNullable)
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
}
