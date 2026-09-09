using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Application.Processamento;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.Infrastructure.Processamento;

/// <summary>
/// Devolve para a fila os eventos que ficaram sem desfecho.
/// </summary>
/// <remarks>
/// <para>
/// Sao tres situacoes, e o supervisor as trata de um jeito so porque, do ponto
/// de vista do evento, elas dizem a mesma coisa — "isto deveria estar acontecendo
/// e nao esta": a retentativa cujo horario chegou, o evento cujo lease de
/// processamento venceu porque a instancia que o pegou caiu, e o recebimento que
/// nunca foi consumido.
/// </para>
/// <para>
/// O supervisor anuncia direto na fila, sem passar pelo outbox. O outbox existe
/// para casar publicacao com uma escrita nova; aqui nao ha escrita nova, e o
/// proprio supervisor ja e a rede de seguranca — uma mensagem perdida volta a
/// ser elegivel no ciclo seguinte. No pior caso um evento e anunciado duas vezes,
/// e a reivindicacao atomica descarta a segunda entrega.
/// </para>
/// </remarks>
public sealed partial class SupervisorReentrega(
    IServiceScopeFactory escopos,
    IFilaProcessamento fila,
    IOptions<OpcoesProcessamento> opcoes,
    TimeProvider relogio,
    ILogger<SupervisorReentrega> logger) : BackgroundService
{
    private readonly OpcoesProcessamento _opcoes = opcoes.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(Math.Max(1, _opcoes.IntervaloSupervisorSegundos));
        var lease = TimeSpan.FromSeconds(Math.Max(1, _opcoes.LeaseProcessamentoSegundos));

        LogIniciado(logger, (int)intervalo.TotalSeconds, (int)lease.TotalSeconds);

        if (_opcoes.RecuperarPendentesNoStartup)
        {
            // Na subida, o lease nao se aplica: o que ficou para tras da execucao
            // anterior deve voltar imediatamente, e nao daqui a alguns minutos.
            await VarrerAsync(TimeSpan.Zero, stoppingToken);
        }

        using var pulso = new PeriodicTimer(intervalo, relogio);

        while (await pulso.WaitForNextTickAsync(stoppingToken))
        {
            await VarrerAsync(lease, stoppingToken);
        }
    }

    private async Task VarrerAsync(TimeSpan lease, CancellationToken stoppingToken)
    {
        try
        {
            using var escopo = escopos.CreateScope();
            var eventos = escopo.ServiceProvider.GetRequiredService<IEventoWebhookRepository>();

            var elegiveis = await eventos.ObterElegiveisParaReentregaAsync(
                relogio.GetUtcNow().UtcDateTime,
                lease,
                Math.Max(1, _opcoes.LimiteReentregaPorCiclo),
                stoppingToken);

            if (elegiveis.Count == 0)
            {
                return;
            }

            foreach (var eventoId in elegiveis)
            {
                await fila.PublicarAsync(new MensagemProcessamento(eventoId), stoppingToken);
            }

            LogReentregues(logger, elegiveis.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Desligamento em andamento.
        }
        catch (Exception excecao)
        {
            LogVarreduraFalhou(logger, excecao);
        }
    }

    [LoggerMessage(EventId = 3201, Level = LogLevel.Information,
        Message = "Supervisor de reentrega iniciado. intervalo_s={IntervaloSegundos} lease_s={LeaseSegundos}")]
    private static partial void LogIniciado(ILogger logger, int intervaloSegundos, int leaseSegundos);

    [LoggerMessage(EventId = 3202, Level = LogLevel.Information,
        Message = "{Total} evento(s) devolvido(s) para a fila.")]
    private static partial void LogReentregues(ILogger logger, int total);

    [LoggerMessage(EventId = 3203, Level = LogLevel.Error, Message = "Varredura do supervisor falhou.")]
    private static partial void LogVarreduraFalhou(ILogger logger, Exception excecao);
}
