using Npgsql;
using Scalar.AspNetCore;
using VinLoggen.Api.Endpoints;

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

// ── HTTP client (used by ProcessLabel → Gemini) ───────────────────────────────
builder.Services.AddHttpClient();

// ── Database ──────────────────────────────────────────────────────────────────
// Reads SUPABASE_CONNECTION_STRING from env / appsettings.
// Falls back to a placeholder so the app starts without a DB configured.
var connectionString = builder.Configuration["SUPABASE_CONNECTION_STRING"]
    ?? string.Empty;

builder.Services.AddNpgsqlDataSource(
    !string.IsNullOrWhiteSpace(connectionString)
        ? connectionString
        : "Host=localhost;Database=vinloggen_placeholder");

// ── Render support ────────────────────────────────────────────────────────────
// Render injects a dynamic $PORT. Map it to ASP.NET Core's listener.
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(renderPort))
{
    builder.WebHost.UseUrls($"http://+:{renderPort}");
}

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opt => opt.WithTitle("VinLoggen API"));
}

app.UseCors("Frontend");

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapHealthEndpoint();
app.MapWineEndpoints();
app.MapProcessLabelEndpoints();

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
