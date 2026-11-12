using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// What every old-country host shares, after .NET Aspire's ServiceDefaults: OpenTelemetry for
/// logs, traces and metrics, health checks, and resilient service-discovered HttpClients.
/// Exports over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (the Aspire dashboard sets it).
/// </summary>
public static class Extensions
{
    private const string HealthPath = "/health";
    private const string AlivePath = "/alive";

    /// <summary>The application's own trace sources (use-case spans from the logging decorator).</summary>
    private static readonly string[] ApplicationSources = ["OldCountry.Api"];

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Outbound calls (YouTube Music, later) retry, time out and break the circuit by default.
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Npgsql"))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(ApplicationSources)
                .AddAspNetCoreInstrumentation(aspnet =>
                    // Probes every few seconds would otherwise be most of the traces.
                    aspnet.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthPath) && !context.Request.Path.StartsWithSegments(AlivePath))
                .AddHttpClientInstrumentation()
                .AddNpgsql());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // "live" answers whether the process should be restarted; everything else, whether it should get traffic.
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    /// <summary><c>/health</c> for readiness (every check) and <c>/alive</c> for liveness (only "live" checks).</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthPath);
        app.MapHealthChecks(AlivePath, new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("live") });
        return app;
    }
}
