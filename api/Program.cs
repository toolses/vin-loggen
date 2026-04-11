using DbUp;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Scalar.AspNetCore;
using VinLoggen.Api.Endpoints;
using VinLoggen.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ── CORS ──────────────────────────────────────────────────────────────────────
// Allows all *.vercel.app origins (preview + production) and localhost.
// For a custom production domain add it to CORS_ALLOWED_ORIGINS (comma-separated).
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy
            .SetIsOriginAllowed(OriginPolicy.IsAllowed)
            .AllowAnyMethod()
            .AllowAnyHeader());
});

// ── OpenAPI ───────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── HTTP client (used by GeminiService) ─────────────────────────────────────
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("gemini");

// ── AI / Gemini ───────────────────────────────────────────────────────────────
builder.Services.AddScoped<GeminiService>();
builder.Services.AddScoped<TasteProfileService>();

// ── Auth (Supabase JWT via JWKS) ─────────────────────────────────────────────
var supabaseUrl = builder.Configuration["SUPABASE_URL"] ?? string.Empty;

if (!string.IsNullOrWhiteSpace(supabaseUrl))
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = $"{supabaseUrl}/auth/v1";
            options.MetadataAddress = $"{supabaseUrl}/auth/v1/.well-known/openid-configuration";
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = $"{supabaseUrl}/auth/v1",
                ValidateAudience = true,
                ValidAudience = "authenticated",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
            };
        });
}
else
{
    // Allow the app to start without auth for local/dev setups
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer();
}
builder.Services.AddAuthorization();

// ── Database ──────────────────────────────────────────────────────────────────
// Reads SUPABASE_CONNECTION_STRING from env / appsettings.
// Falls back to a placeholder so the app starts without a DB configured.
var connectionString = builder.Configuration["SUPABASE_CONNECTION_STRING"]
    ?? string.Empty;

if (!string.IsNullOrWhiteSpace(connectionString))
{
    var csBuilder = new NpgsqlConnectionStringBuilder(connectionString)
    {
        MaxPoolSize = 10,
        MinPoolSize = 1,
        ConnectionIdleLifetime = 300, // seconds – reclaim idle connections
        Timeout = 15,                 // connect timeout in seconds
        CommandTimeout = 30,          // query timeout in seconds
    };
    builder.Services.AddNpgsqlDataSource(csBuilder.ConnectionString);
}
else
{
    builder.Services.AddNpgsqlDataSource("Host=localhost;Database=vinloggen_placeholder");
}

// ── Render support ────────────────────────────────────────────────────────────
// Render injects a dynamic $PORT. Map it to ASP.NET Core's listener.
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(renderPort))
{
    builder.WebHost.UseUrls($"http://+:{renderPort}");
}

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Database Migrations (DbUp) ────────────────────────────────────────────────
if (!string.IsNullOrWhiteSpace(connectionString))
{
    var upgrader = DeployChanges.To
        .PostgresqlDatabase(connectionString)
        .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly)
        .LogToConsole()
        .Build();

    var result = upgrader.PerformUpgrade();

    if (!result.Successful)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Database migration failed: {result.Error}");
        Console.ResetColor();
        return;
    }

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Database migrations applied successfully.");
    Console.ResetColor();
}

// ── Middleware ────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opt => opt.WithTitle("VinLoggen API"));
}

app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapHealthEndpoint();
app.MapWineEndpoints();
app.MapProcessLabelEndpoints();
app.MapWineAnalyzeEndpoints();
app.MapTasteProfileEndpoints();

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────────
static class OriginPolicy
{
    private static readonly string[] _extra = (
        Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS") ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool IsAllowed(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        return uri.Host is "localhost" or "127.0.0.1"
            || uri.Host.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase)
            || _extra.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
    }
}
