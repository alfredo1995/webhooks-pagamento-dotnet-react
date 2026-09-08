using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Contratos;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.Application.Processamento;

public interface IProcessadorPagamento
{
    Task ProcessarAsync(Guid eventoId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A regra de negocio "pesada" que roda fora do ciclo da requisicao HTTP.
/// </summary>
/// <remarks>
/// O metodo e idempotente por construcao: um evento ja processado e ignorado.
/// Isso importa porque a fila pode reentregar o mesmo id — no restart da
/// aplicacao, por exemplo, quando os pendentes sao reenfileirados.
/// </remarks>
public sealed partial class ProcessadorPagamento(
    IEventoWebhookRepository eventos,
    IStatusContratoRepository contratos,
    IUnitOfWork unitOfWork,
    IOptions<OpcoesProcessamento> opcoes,
    ILogger<ProcessadorPagamento> logger) : IProcessadorPagamento
{
    private readonly OpcoesProcessamento _opcoes = opcoes.Value;

    public async Task ProcessarAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        var evento = await eventos.ObterPorIdAsync(eventoId, cancellationToken);
        if (evento is null)
        {
            LogEventoInexistente(logger, eventoId);
            return;
        }

        if (evento.Status is StatusProcessamento.Processado or StatusProcessamento.Invalido)
        {
            LogJaConcluido(logger, evento.IdTransacao, evento.Status.ToString());
            return;
        }

        evento.IniciarProcessamento();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var cronometro = Stopwatch.StartNew();

        try
        {
            // Simula a regra de negocio custosa exigida pelo desafio.
            if (_opcoes.AtrasoSimuladoMs > 0)
            {
                await Task.Delay(_opcoes.AtrasoSimuladoMs, cancellationToken);
            }

            await AplicarNoContratoAsync(evento, cancellationToken);

            cronometro.Stop();
            evento.ConcluirComSucesso(cronometro.ElapsedMilliseconds);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            LogProcessado(logger, evento.IdTransacao, cronometro.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Desligamento em andamento: nao marca falha, o evento volta como pendente.
            throw;
        }
        catch (Exception excecao)
        {
            cronometro.Stop();
            evento.RegistrarFalha(excecao.Message, cronometro.ElapsedMilliseconds);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            LogFalha(logger, excecao, evento.IdTransacao, evento.Tentativas);
        }
    }

    private async Task AplicarNoContratoAsync(EventoWebhook evento, CancellationToken cancellationToken)
    {
        if (evento.IdContrato is null || evento.Valor is null
            || evento.DataPagamento is null || evento.StatusPagamento is null)
        {
            throw new DomainException("Evento sem dados validados nao pode ser aplicado ao contrato.");
        }

        var contrato = await contratos.ObterPorContratoAsync(evento.IdContrato, cancellationToken);
        if (contrato is null)
        {
            contrato = StatusContrato.Novo(evento.IdContrato);
            await contratos.AdicionarAsync(contrato, cancellationToken);
        }

        contrato.AplicarPagamento(
            evento.IdTransacao,
            evento.Valor.Value,
            evento.DataPagamento.Value,
            evento.StatusPagamento.Value);
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Evento processado. id_transacao={IdTransacao} duracao_ms={DuracaoMs}")]
    private static partial void LogProcessado(ILogger logger, string idTransacao, long duracaoMs);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error,
        Message = "Falha ao processar evento. id_transacao={IdTransacao} tentativa={Tentativas}")]
    private static partial void LogFalha(ILogger logger, Exception excecao, string idTransacao, int tentativas);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning,
        Message = "Evento {EventoId} nao encontrado ao processar.")]
    private static partial void LogEventoInexistente(ILogger logger, Guid eventoId);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Debug,
        Message = "Evento ja concluido, reentrega ignorada. id_transacao={IdTransacao} status={Status}")]
    private static partial void LogJaConcluido(ILogger logger, string idTransacao, string status);
}
