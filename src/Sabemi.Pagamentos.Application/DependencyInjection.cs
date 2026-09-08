using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Sabemi.Pagamentos.Application.Consultas;
using Sabemi.Pagamentos.Application.Processamento;
using Sabemi.Pagamentos.Application.Webhooks;

namespace Sabemi.Pagamentos.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registra casos de uso e validadores. Esta camada nao conhece EF Core,
    /// Channel nem ASP.NET: apenas as portas declaradas no dominio.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IRecebimentoWebhookService, RecebimentoWebhookService>();
        services.AddScoped<IProcessadorPagamento, ProcessadorPagamento>();
        services.AddScoped<IConsultaService, ConsultaService>();

        services.AddScoped<IValidator<PagamentoWebhookRequest>, PagamentoWebhookValidator>();

        return services;
    }
}
