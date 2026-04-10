using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddHttpClient();

        // Register Npgsql connection pool.
        // Connection string is set in local.settings.json (dev) or
        // Azure Static Web Apps application settings (prod).
        var connectionString = context.Configuration["SUPABASE_CONNECTION_STRING"]
            ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddNpgsqlDataSource(connectionString);
        }
        else
        {
            // Register a no-op data source so DI doesn't fail during local runs
            // without a DB connection. Functions will return 503 if invoked.
            services.AddSingleton(NpgsqlDataSource.Create("Host=localhost;Database=placeholder"));
        }
    })
    .Build();

await host.RunAsync();
