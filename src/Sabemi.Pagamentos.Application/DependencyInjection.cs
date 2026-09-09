using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sabemi.Pagamentos.Application.Consultas;
using Sabemi.Pagamentos.Application.DeadLetters;
using Sabemi.Pagamentos.Application.Processamento;
using Sabemi.Pagamentos.Application.Webhooks;

namespace Sabemi.Pagamentos.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registra casos de uso e validadores. Esta camada nao conhece EF Core,
    /// Channel, RabbitMQ nem ASP.NET: apenas as portas declaradas no dominio.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IRecebimentoWebhookService, RecebimentoWebhookService>();
        services.AddScoped<IProcessadorPagamento, ProcessadorPagamento>();
        services.AddScoped<IConsultaService, ConsultaService>();
        services.AddScoped<IReprocessamentoService, ReprocessamentoService>();

        services.AddSingleton<IPoliticaRetentativa, PoliticaRetentativaExponencial>();

        // O relogio entra por injecao para que backoff e janela de rotacao de
        // segredo possam ser testados sem esperar o tempo passar de verdade.
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IValidator<PagamentoWebhookRequest>, PagamentoWebhookValidator>();

        return services;
    }
}
