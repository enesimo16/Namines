using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Namines.API.Extensions;
using Namines.API.Middleware;
using Namines.Core.Models.Auth;
using Namines.Infrastructure.Data;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ── Load appsettings.secrets.json (git-ignored local secrets) ──────────────
// This file contains Jwt:Key, Groq:ApiKey etc. — NEVER commit it.
builder.Configuration
    .AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: false);
// Environment variables override everything (for Docker / cloud deployments)
// e.g. set GROQ__APIKEY and JWT__KEY in your environment
builder.Configuration.AddEnvironmentVariables();

// Configure QuestPDF license
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Configure Database
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=namines_auth.db"));

// Configure Identity Core
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 4;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<AuthDbContext>()
.AddDefaultTokenProviders();

// Configure JWT Authentication
var secretKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(secretKey))
{
    // Development fallback — will be overridden by appsettings.secrets.json or env var JWT__KEY
    secretKey = "NaminesDevFallbackKey_Change_In_Production_Min32Chars!";
    Console.WriteLine("[UYARI] Jwt:Key appsettings.secrets.json veya ortam degiskeni ile tanimlanmamis. Gelistirme fallback'i kullaniliyor.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "NaminesServer",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "NaminesClient",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
});

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        // Output in camelCase so frontend TypeScript types match
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register SignalR real-time collaboration services
builder.Services.AddSignalR();

// Setup CORS for Next.js frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowNextJs", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:3001", "http://127.0.0.1:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // WebSocket ve SignalR handshake desteği için kritik
    });
});

// Force Kestrel to listen on port 5000
builder.WebHost.UseUrls("http://localhost:5000");

// Setup custom services
builder.Services.AddNaminesServices();

var app = builder.Build();

// Auto-create/migrate database on startup
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        dbContext.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Veritabanı oluşturma hatası: {ex.Message}");
    }
}

// Use global exception handler
app.UseMiddleware<ExceptionMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection();

app.UseWebSockets();
app.UseCors("AllowNextJs");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<Namines.API.Hubs.CanvasHub>("/hubs/canvas");

app.Run();
