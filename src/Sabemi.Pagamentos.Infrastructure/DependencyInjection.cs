using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sabemi.Pagamentos.Application.Processamento;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Contratos;
using Sabemi.Pagamentos.Domain.Eventos;
using Sabemi.Pagamentos.Infrastructure.Persistence;
using Sabemi.Pagamentos.Infrastructure.Persistence.Repositories;
using Sabemi.Pagamentos.Infrastructure.Processamento;

namespace Sabemi.Pagamentos.Infrastructure;

public static class DependencyInjection
{
    public const string NomeConnectionString = "SqlServer";

    /// <summary>
    /// Registra persistencia e processamento assincrono. Trocar de banco ou de
    /// fila exige mudar apenas este arquivo.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(NomeConnectionString)
            ?? throw new InvalidOperationException($"Connection string '{NomeConnectionString}' nao configurada.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
            }));

        return services.AddPersistencia().AddProcessamento(configuration);
    }

    /// <summary>Registros independentes do provider (reaproveitados pelos testes).</summary>
    public static IServiceCollection AddPersistencia(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IEventoWebhookRepository, EventoWebhookRepository>();
        services.AddScoped<IStatusContratoRepository, StatusContratoRepository>();

        return services;
    }

    public static IServiceCollection AddProcessamento(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<OpcoesProcessamento>(configuration.GetSection(OpcoesProcessamento.Secao));

        // A fila e singleton: e o ponto de encontro entre a borda HTTP e o worker.
        services.AddSingleton<FilaProcessamentoEmMemoria>();
        services.AddSingleton<IFilaProcessamento>(sp => sp.GetRequiredService<FilaProcessamentoEmMemoria>());
        services.AddHostedService<WorkerProcessamento>();

        return services;
    }
}
