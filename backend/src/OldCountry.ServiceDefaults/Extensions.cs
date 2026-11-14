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
/// What all old-country hosts share, based on the Aspire ServiceDefaults: OpenTelemetry for logs,
/// traces and metrics, health checks, and resilient HttpClients with service discovery.
/// It exports over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (the Aspire dashboard sets it).
/// </summary>
public static class Extensions
{
    private const string HealthPath = "/health";
    private const string AlivePath = "/alive";

    /// <summary>The trace sources of the app itself: one span for each use case (logging decorator) and each yt-dlp run.</summary>
    private static readonly string[] ApplicationSources = ["OldCountry.Application", "OldCountry.Infrastructure.YtDlp"];

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Outbound calls (to YouTube Music, later) retry, stop after a timeout and break the circuit by default.
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
                .AddMeter("Npgsql")
                // The metrics of old-country itself (AppMetrics.MeterName in Application).
                .AddMeter("OldCountry.Application")
                // SignalR: the open connections and their durations.
                .AddMeter("Microsoft.AspNetCore.Http.Connections"))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(ApplicationSources)
                // SignalR: a span for each hub method call, connect and disconnect.
                .AddSource("Microsoft.AspNetCore.SignalR.Server")
                .AddAspNetCoreInstrumentation(aspnet =>
                    // Do not trace health probes. They occur every few seconds and would hide the real requests.
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
        // "live" tells if the process must restart. All other checks tell if it can get traffic.
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    /// <summary><c>/health</c> for readiness (all checks) and <c>/alive</c> for liveness (only the "live" checks).</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthPath);
        app.MapHealthChecks(AlivePath, new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("live") });
        return app;
    }
}
