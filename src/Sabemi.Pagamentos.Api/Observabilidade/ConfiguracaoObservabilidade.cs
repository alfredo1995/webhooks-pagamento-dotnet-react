using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Sabemi.Pagamentos.Application.Observabilidade;

namespace Sabemi.Pagamentos.Api.Observabilidade;

public sealed class OpcoesObservabilidade
{
    public const string Secao = "Observabilidade";

    public string NomeServico { get; set; } = "sabemi-pagamentos-api";

    /// <summary>Endpoint OTLP do coletor. Vazio, a aplicacao instrumenta mas nao exporta.</summary>
    public string? EndpointOtlp { get; set; }
}

/// <summary>
/// Liga a instrumentacao da aplicacao ao OpenTelemetry.
/// </summary>
/// <remarks>
/// <para>
/// A composicao mora na borda de proposito: e a API que decide para onde os
/// dados vao. As camadas de dentro so publicam em <see cref="Telemetria"/>, que
/// e BCL pura — trocar OpenTelemetry por outra coisa nao encosta em nenhum caso
/// de uso.
/// </para>
/// <para>
/// O trace nao termina no <c>202</c>. O <c>traceparent</c> da requisicao viaja no
/// outbox e nos cabecalhos da mensagem, e o processamento em background abre sua
/// atividade como filha dele. E o que permite abrir um trace no Jaeger e ver, em
/// uma linha do tempo so, a chamada do banco parceiro, a publicacao, o consumo,
/// as retentativas e a ida para a dead-letter queue.
/// </para>
/// </remarks>
public static class ConfiguracaoObservabilidade
{
    public static IServiceCollection AddObservabilidade(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var secao = configuration.GetSection(OpcoesObservabilidade.Secao);
        services.Configure<OpcoesObservabilidade>(secao);

        var opcoes = secao.Get<OpcoesObservabilidade>() ?? new OpcoesObservabilidade();
        var exporta = !string.IsNullOrWhiteSpace(opcoes.EndpointOtlp);

        services.AddOpenTelemetry()
            .ConfigureResource(recurso => recurso.AddService(opcoes.NomeServico, serviceVersion: "1.0.0"))
            .WithTracing(tracing =>
            {
                tracing
                    .AddSource(Telemetria.Nome)
                    .AddAspNetCoreInstrumentation(instrumentacao =>
                    {
                        // Health check e polling de infraestrutura: ruido que
                        // esconderia o trace que interessa.
                        instrumentacao.Filter = contexto =>
                            !contexto.Request.Path.StartsWithSegments("/health");
                        instrumentacao.RecordException = true;
                    })
                    .AddHttpClientInstrumentation();

                if (exporta)
                {
                    tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(opcoes.EndpointOtlp!));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(Telemetria.Nome)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (exporta)
                {
                    metrics.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(opcoes.EndpointOtlp!));
                }
            });

        return services;
    }
}
