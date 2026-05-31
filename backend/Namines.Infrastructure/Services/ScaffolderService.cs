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
                    var entityContent = GenerateEntityCode(table);
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

    private string GenerateEntityCode(SchemaTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
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
            sb.AppendLine($"    public DbSet<{table.Name}> {table.Name}s {{ get; set; }}");
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
        sb.AppendLine($"        return await _context.{table.Name}s.ToListAsync();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [HttpGet(\"{id}\")]");
        sb.AppendLine($"    public async Task<ActionResult<{table.Name}>> GetById({pkType} id)");
        sb.AppendLine("    {");
        sb.AppendLine($"        var entity = await _context.{table.Name}s.FindAsync(id);");
        sb.AppendLine("        if (entity == null) return NotFound();");
        sb.AppendLine("        return entity;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    [HttpPost]");
        sb.AppendLine($"    public async Task<ActionResult<{table.Name}>> Create([FromBody] {table.Name} entity)");
        sb.AppendLine("    {");
        sb.AppendLine($"        _context.{table.Name}s.Add(entity);");
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
        sb.AppendLine($"        var entity = await _context.{table.Name}s.FindAsync(id);");
        sb.AppendLine("        if (entity == null) return NotFound();");
        sb.AppendLine();
        sb.AppendLine($"        _context.{table.Name}s.Remove(entity);");
        sb.AppendLine("        await _context.SaveChangesAsync();");
        sb.AppendLine("        return NoContent();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine($"    private Task<bool> EntityExists({pkType} id)");
        sb.AppendLine("    {");
        sb.AppendLine($"        return _context.{table.Name}s.AnyAsync(e => e.{pkName} == id);");
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

// Add services to the container.
builder.Services.AddControllers();
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
            return BadRequest(new { error = ""Soru alanı boş olamaz"" });
        }

        string sql = TranslateToSql(request.Question);

        if (string.IsNullOrEmpty(sql) || !sql.TrimStart().StartsWith(""SELECT"", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = ""Güvenlik nedeniyle sadece salt-okunur SELECT sorguları çalıştırılabilir."" });
        }

        var forbiddenKeywords = new[] { ""INSERT"", ""UPDATE"", ""DELETE"", ""DROP"", ""ALTER"", ""CREATE"", ""TRUNCATE"" };
        if (forbiddenKeywords.Any(k => sql.ToUpperInvariant().Contains(k)))
        {
            return BadRequest(new { error = ""Geçersiz SQL ifadesi. Desteklenmeyen komutlar tespit edildi."" });
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
            var xAxisKey = firstRow?.Keys.FirstOrDefault(k => k.ToLower().Contains(""name"") || k.ToLower().Contains(""date"") || k.ToLower().Contains(""title"") || k.ToLower().Contains(""id"")) ?? firstRow?.Keys.FirstOrDefault() ?? """";
            var yAxisKey = firstRow?.Keys.FirstOrDefault(k => !string.Equals(k, xAxisKey, StringComparison.OrdinalIgnoreCase) && (k.ToLower().Contains(""count"") || k.ToLower().Contains(""total"") || k.ToLower().Contains(""price"") || k.ToLower().Contains(""amount"") || k.ToLower().Contains(""quantity"") || k.ToLower().Contains(""id""))) ?? firstRow?.Keys.FirstOrDefault(k => !string.Equals(k, xAxisKey, StringComparison.OrdinalIgnoreCase)) ?? """";

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
            return StatusCode(500, new { error = $""SQL Çalıştırma Hatası: {ex.Message}"", sql = sql });
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

        if (q.Contains(""toplam"") || q.Contains(""en çok"") || q.Contains(""en yüksek""))
        {
            return $""SELECT {primaryTable}.*, COUNT(*) as Toplam FROM {primaryTable} GROUP BY 1 ORDER BY Toplam DESC LIMIT 10"";
        }
        
        if (q.Contains(""son"") || q.Contains(""tarih"") || q.Contains(""yeni""))
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
          error: result.error || 'Analiz sırasında bir hata oluştu.'
        }]);
      }
    } catch (err) {
      setMessages(prev => [...prev, {
        id: Date.now() + 1,
        sender: 'assistant',
        error: 'API bağlantısı başarısız oldu.'
      }]);
    } finally {
      setIsLoading(false);
    }
  };

  const renderChart = (msg: any) => {
    if (!msg.data || msg.data.length === 0) return <div className=""text-zinc-500 text-xs py-4 text-center"">Gösterilecek veri yok.</div>;

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
            <h3 className=""text-sm font-bold tracking-wide"">AI-BI Veri Analitiği</h3>
            <p className=""text-[10px] text-zinc-400"">Doğal dille sor, grafikle gör.</p>
          </div>
        </div>
      </div>

      <div className=""flex-1 overflow-y-auto p-4 space-y-4"">
        {messages.length === 0 && (
          <div className=""h-full flex flex-col items-center justify-center text-center p-6 space-y-2"">
            <Database className=""w-10 h-10 text-zinc-700 animate-bounce"" />
            <p className=""text-xs text-zinc-400"">""Toplam sipariş sayısını göster"" veya ""En yeni üyeleri listele"" gibi sorular sorabilirsiniz.</p>
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
                  <div className=""bg-zinc-950 p-2 rounded-lg border border-zinc-800/80 font-mono text-[10px] text-indigo-300 overflow-x-auto flex flex-col"">
                    <span className=""text-[8px] font-extrabold text-zinc-500 uppercase tracking-widest mb-1 select-none"">Üretilen SQL</span>
                    {msg.sql}
                  </div>
                  
                  <div className=""bg-zinc-950/45 p-2 rounded-xl border border-zinc-800/40"">
                    <div className=""flex justify-between items-center mb-2 px-1"">
                      <span className=""text-[9px] font-bold text-zinc-400 uppercase tracking-wide flex items-center gap-1"">
                        {msg.chartType === 'bar' && <BarChart2 className=""w-3.5 h-3.5 text-indigo-400"" />}
                        {msg.chartType === 'pie' && <PieIcon className=""w-3.5 h-3.5 text-emerald-400"" />}
                        {msg.chartType === 'line' && <LineIcon className=""w-3.5 h-3.5 text-sky-400"" />}
                        {msg.chartType === 'table' && <Table className=""w-3.5 h-3.5 text-amber-400"" />}
                        Görsel Rapor ({msg.chartType})
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
            <span>AI Verileri analiz ediyor...</span>
          </div>
        )}
      </div>

      <form onSubmit={handleAsk} className=""border-t border-zinc-800/60 p-3 bg-zinc-900/45 flex gap-2"">
        <input
          type=""text""
          placeholder=""Veritabanı hakkında bir soru yazın...""
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          disabled={isLoading}
          className=""bg-zinc-950 border border-zinc-800 rounded-xl px-3 py-2 text-xs text-zinc-100 placeholder-zinc-500 focus:outline-none focus:border-indigo-500 flex-1 min-w-0""
        />
        <button
          type=""submit""
          disabled={isLoading}
          className=""bg-indigo-600 hover:bg-indigo-500 text-white p-2 rounded-xl flex items-center justify-center transition-colors disabled:opacity-50""
        >
          <Send className=""w-4 h-4"" />
        </button>
      </form>
    </div>
  );
}
";
    }
}
