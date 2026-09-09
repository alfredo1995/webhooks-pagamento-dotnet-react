using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Application.Observabilidade;
using Sabemi.Pagamentos.Application.Processamento;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Outbox;

namespace Sabemi.Pagamentos.Infrastructure.Processamento;

/// <summary>
/// Leva para a fila as mensagens que o recebimento gravou no outbox.
/// </summary>
/// <remarks>
/// <para>
/// E a segunda metade do padrao outbox. A primeira — gravar mensagem e evento na
/// mesma transacao — garante que nada seja anunciado sem existir; esta garante
/// que nada que existe deixe de ser anunciado, porque a mensagem so sai da tabela
/// depois que o broker aceitou.
/// </para>
/// <para>
/// A entrega e "pelo menos uma vez", nunca "exatamente uma vez": se o broker
/// aceitar e o processo cair antes de marcar a mensagem, ela sera publicada de
/// novo. Perseguir exatamente-uma-vez aqui seria caro e inutil — quem resolve a
/// duplicidade e a reivindicacao atomica do evento, que ja precisa existir de
/// qualquer forma por causa da reentrega do proprio broker.
/// </para>
/// </remarks>
public sealed partial class PublicadorOutbox(
    IServiceScopeFactory escopos,
    IFilaProcessamento fila,
    IOptions<OpcoesProcessamento> opcoes,
    TimeProvider relogio,
    ILogger<PublicadorOutbox> logger) : BackgroundService
{
    /// <summary>Publicadas ha mais de um dia nao servem mais a nada: viram peso morto no indice.</summary>
    private static readonly TimeSpan RetencaoPublicadas = TimeSpan.FromDays(1);

    private readonly OpcoesProcessamento _opcoes = opcoes.Value;

    private DateTime _proximoExpurgoUtc = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromMilliseconds(Math.Max(100, _opcoes.Outbox.IntervaloMs));

        LogIniciado(logger, _opcoes.Outbox.TamanhoLote, (int)intervalo.TotalMilliseconds);

        using var pulso = new PeriodicTimer(intervalo, relogio);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var publicadas = await PublicarLoteAsync(stoppingToken);

                // Enquanto houver fila acumulada, nao espera o proximo pulso.
                if (publicadas >= _opcoes.Outbox.TamanhoLote)
                {
                    continue;
                }

                await ExpurgarSePrecisoAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception excecao)
            {
                // Banco fora do ar, por exemplo. O publicador nao pode morrer:
                // as mensagens continuam gravadas e o proximo ciclo as pega.
                LogCicloFalhou(logger, excecao);
            }

            if (!await pulso.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    private async Task<int> PublicarLoteAsync(CancellationToken stoppingToken)
    {
        using var escopo = escopos.CreateScope();

        var outbox = escopo.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var unitOfWork = escopo.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var agora = relogio.GetUtcNow().UtcDateTime;

        var lote = await outbox.ReservarLoteAsync(
            Guid.NewGuid().ToString("N"),
            agora,
            TimeSpan.FromSeconds(Math.Max(5, _opcoes.Outbox.ReservaSegundos)),
            Math.Max(1, _opcoes.Outbox.TamanhoLote),
            stoppingToken);

        if (lote.Count == 0)
        {
            return 0;
        }

        var publicadas = 0;

        foreach (var mensagem in lote)
        {
            try
            {
                await fila.PublicarAsync(
                    new MensagemProcessamento(mensagem.EventoId, mensagem.TraceParent, mensagem.TraceState),
                    stoppingToken);

                mensagem.MarcarPublicada();
                publicadas++;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception excecao)
            {
                mensagem.RegistrarFalhaDePublicacao(
                    excecao.Message,
                    agora.AddSeconds(Math.Max(1, _opcoes.Outbox.BackoffPublicacaoSegundos)));

                LogPublicacaoFalhou(logger, excecao, mensagem.EventoId);
            }
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);

        if (publicadas > 0)
        {
            Telemetria.MensagensPublicadas.Add(publicadas);
        }

        return publicadas;
    }

    private async Task ExpurgarSePrecisoAsync(CancellationToken stoppingToken)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        if (agora < _proximoExpurgoUtc)
        {
            return;
        }

        _proximoExpurgoUtc = agora.AddHours(1);

        using var escopo = escopos.CreateScope();
        var outbox = escopo.ServiceProvider.GetRequiredService<IOutboxRepository>();

        var removidas = await outbox.RemoverPublicadasAsync(agora - RetencaoPublicadas, stoppingToken);
        if (removidas > 0)
        {
            LogExpurgo(logger, removidas);
        }
    }

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information,
        Message = "Publicador do outbox iniciado. lote={TamanhoLote} intervalo_ms={IntervaloMs}")]
    private static partial void LogIniciado(ILogger logger, int tamanhoLote, int intervaloMs);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Warning,
        Message = "Falha ao publicar mensagem do outbox. evento_id={EventoId}")]
    private static partial void LogPublicacaoFalhou(ILogger logger, Exception excecao, Guid eventoId);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Error, Message = "Ciclo do publicador do outbox falhou.")]
    private static partial void LogCicloFalhou(ILogger logger, Exception excecao);

    [LoggerMessage(EventId = 3104, Level = LogLevel.Debug,
        Message = "{Total} mensagem(ns) publicada(s) removida(s) do outbox.")]
    private static partial void LogExpurgo(ILogger logger, int total);
}
