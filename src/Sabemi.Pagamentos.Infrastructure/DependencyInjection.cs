using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sabemi.Pagamentos.Application.Processamento;
using Sabemi.Pagamentos.Domain.Auditoria;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Contratos;
using Sabemi.Pagamentos.Domain.Eventos;
using Sabemi.Pagamentos.Domain.Outbox;
using Sabemi.Pagamentos.Infrastructure.Persistence;
using Sabemi.Pagamentos.Infrastructure.Persistence.Repositories;
using Sabemi.Pagamentos.Infrastructure.Processamento;
using Sabemi.Pagamentos.Infrastructure.Processamento.RabbitMq;

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

        // Vazia e o caso comum, nao nula: o appsettings declara a chave sem valor
        // justamente para nao versionar a senha do banco. Checar so por null
        // deixaria a string vazia chegar ao provider e falhar la, com uma
        // mensagem que nao diz o que fazer.
        var connectionString = configuration.GetConnectionString(NomeConnectionString);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{NomeConnectionString}' nao configurada. Defina MSSQL_SA_PASSWORD "
                + "no .env (compose) ou rode ./tools/configurar-segredos-locais.sh para o dotnet run local.");
        }

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
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IDeadLetterRepository, DeadLetterRepository>();
        services.AddScoped<IAuditoriaRepository, AuditoriaRepository>();

        return services;
    }

    /// <summary>
    /// Monta o pipeline assincrono: publicador do outbox, supervisor de reentrega
    /// e o par fila/consumidor escolhido por configuracao.
    /// </summary>
    /// <remarks>
    /// O provedor de fila e a unica decisao que muda entre "uma instancia" e
    /// "varias instancias". Como o restante do pipeline fala apenas com
    /// <see cref="IFilaProcessamento"/>, trocar <c>Memoria</c> por <c>RabbitMq</c>
    /// nao altera nenhum caso de uso.
    /// </remarks>
    public static IServiceCollection AddProcessamento(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var secao = configuration.GetSection(OpcoesProcessamento.Secao);
        services.Configure<OpcoesProcessamento>(secao);

        if (secao.GetValue("Fila", ProvedorFila.Memoria) == ProvedorFila.RabbitMq)
        {
            services.AddSingleton<ConexaoRabbitMq>();
            services.AddSingleton<FilaProcessamentoRabbitMq>();
            services.AddSingleton<IFilaProcessamento>(sp => sp.GetRequiredService<FilaProcessamentoRabbitMq>());
            services.AddHostedService<ConsumidorRabbitMq>();
        }
        else
        {
            // A fila e singleton: e o ponto de encontro entre o publicador e o worker.
            services.AddSingleton<FilaProcessamentoEmMemoria>();
            services.AddSingleton<IFilaProcessamento>(sp => sp.GetRequiredService<FilaProcessamentoEmMemoria>());
            services.AddHostedService<WorkerProcessamento>();
        }

        services.AddHostedService<PublicadorOutbox>();
        services.AddHostedService<SupervisorReentrega>();

        return services;
    }
}
