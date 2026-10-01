using EquityLens.Api.Observability;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EquityLens.Api.Bootstrap;

public static class ObservabilityRegistration
{
    public static void AddEquityLensObservability(this WebApplicationBuilder builder)
    {
        const string serviceName = "equitylens-api";
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        var resourceBuilder = ResourceBuilder.CreateDefault().AddService(serviceName);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(EquityLensTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.EnrichWithHttpRequestMessage = (activity, request) =>
                        {
                            if (request.RequestUri is not null)
                            {
                                activity.SetTag("url.full", request.RequestUri.GetLeftPart(UriPartial.Path));
                            }
                        };
                    });

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(EquityLensTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
                }
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(resourceBuilder);
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                logging.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
            }
        });
    }
}
