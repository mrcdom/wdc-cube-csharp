using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Shopping.Domain;
using Shopping.Persistence;

namespace Shopping.Hosting;

/// <summary>
/// Health checks and metrics for a Shopping web host.
/// </summary>
/// <remarks>
/// <para><c>/health/live</c> answers 200 while the process serves requests; <c>/health/ready</c> answers 503 while the
/// database does not answer, so a load balancer stops sending requests that would fail.</para>
/// <para>The metrics go out through OTLP only when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is configured (the standard
/// OpenTelemetry variables apply: protocol, export interval, resource attributes). Each instance reports as the
/// service named after the application, with the machine name as its instance id.</para>
/// </remarks>
public static class ShoppingOperations
{
    public const string LivePath = "/health/live";

    public const string ReadyPath = "/health/ready";

    private const string Ready = "ready";

    /// <param name="meters">The meters of the host's libraries, besides the application's own.</param>
    public static IHostApplicationBuilder AddShoppingOperations(this IHostApplicationBuilder builder, ShoppingStore store,
        params string[] meters)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(meters);
        builder.Services.AddHealthChecks()
            .Add(new HealthCheckRegistration("database", _ => new DatabaseHealthCheck(store), HealthStatus.Unhealthy, [Ready],
                DatabaseHealthCheck.Timeout));

        if (builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is { Length: > 0 })
        {
            builder.Services.AddOpenTelemetry()
                .ConfigureResource(r => r.AddService(builder.Environment.ApplicationName,
                    serviceInstanceId: Environment.MachineName))
                .WithMetrics(m => m
                    .AddMeter([ShoppingMetrics.MeterName, .. meters])
                    .AddAspNetCoreInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter());
        }

        return builder;
    }

    public static IEndpointRouteBuilder MapShoppingHealth(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks(ReadyPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains(Ready) });
        return endpoints;
    }
}

/// <summary>Healthy while a round trip to the database succeeds within <see cref="Timeout"/>.</summary>
internal sealed class DatabaseHealthCheck(ShoppingStore store) : IHealthCheck
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await store.Ping(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("The database does not answer.", e);
        }
    }
}
