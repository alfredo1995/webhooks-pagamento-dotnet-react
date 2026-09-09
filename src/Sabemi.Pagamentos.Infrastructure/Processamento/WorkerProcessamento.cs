using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Application.Processamento;

namespace Sabemi.Pagamentos.Infrastructure.Processamento;

/// <summary>
/// Consome a fila em memoria fora do ciclo da requisicao HTTP.
/// </summary>
/// <remarks>
/// Cada evento roda em seu proprio escopo de DI, porque o <c>DbContext</c> e
/// scoped e nao pode ser compartilhado entre processamentos concorrentes. Quem
/// devolve para a fila o que ficou sem desfecho e o supervisor, nao este worker:
/// recuperacao e responsabilidade de um so lugar.
/// </remarks>
public sealed partial class WorkerProcessamento(
    FilaProcessamentoEmMemoria fila,
    IServiceScopeFactory escopos,
    IOptions<OpcoesProcessamento> opcoes,
    ILogger<WorkerProcessamento> logger) : BackgroundService
{
    private readonly OpcoesProcessamento _opcoes = opcoes.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogIniciado(logger, _opcoes.Concorrencia, _opcoes.AtrasoSimuladoMs);

        var consumidores = Enumerable
            .Range(0, Math.Max(1, _opcoes.Concorrencia))
            .Select(indice => ConsumirAsync(indice, stoppingToken));

        await Task.WhenAll(consumidores);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        fila.Encerrar();

        await base.StopAsync(cancellationToken);
    }

    private async Task ConsumirAsync(int indice, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var mensagem in fila.LerAsync(stoppingToken))
            {
                fila.ConfirmarRetirada();

                await ProcessarComEscopoAsync(mensagem, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogConsumidorEncerrado(logger, indice);
        }
    }

    private async Task ProcessarComEscopoAsync(MensagemProcessamento mensagem, CancellationToken stoppingToken)
    {
        using var escopo = escopos.CreateScope();

        try
        {
            var processador = escopo.ServiceProvider.GetRequiredService<IProcessadorPagamento>();

            await processador.ProcessarAsync(mensagem, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception excecao)
        {
            // O processador ja trata falha de negocio no proprio evento. Chegar
            // aqui significa erro fora dele (infra, DI); o worker nao pode morrer
            // por isso, e o lease devolve o evento para a fila mais tarde.
            LogErroInesperado(logger, excecao, mensagem.EventoId);
        }
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information,
        Message = "Worker de processamento iniciado. concorrencia={Concorrencia} atraso_simulado_ms={AtrasoMs}")]
    private static partial void LogIniciado(ILogger logger, int concorrencia, int atrasoMs);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Error,
        Message = "Erro inesperado fora do processador ao tratar o evento {EventoId}.")]
    private static partial void LogErroInesperado(ILogger logger, Exception excecao, Guid eventoId);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Information, Message = "Consumidor {Indice} encerrado.")]
    private static partial void LogConsumidorEncerrado(ILogger logger, int indice);
}
