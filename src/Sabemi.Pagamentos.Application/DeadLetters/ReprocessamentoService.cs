using Microsoft.Extensions.Logging;
using Sabemi.Pagamentos.Application.Observabilidade;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Eventos;
using Sabemi.Pagamentos.Domain.Outbox;

namespace Sabemi.Pagamentos.Application.DeadLetters;

public interface IReprocessamentoService
{
    Task<ReprocessamentoAceito> ReprocessarAsync(
        Guid eventoId,
        string usuario,
        CancellationToken cancellationToken = default);
}

public sealed record ReprocessamentoAceito(Guid EventoId, string IdTransacao, string Mensagem);

/// <summary>
/// Devolve para a fila um evento que parou na dead-letter queue.
/// </summary>
/// <remarks>
/// O reenvio passa pelo mesmo outbox do recebimento, e nao por uma publicacao
/// direta: se a mensagem fosse para o broker fora da transacao, um erro no
/// commit deixaria a carta marcada como reprocessada e o evento parado. Vale
/// tambem a regra inversa — so sai da DLQ evento em <c>Falha</c>, entao clicar
/// duas vezes em "reprocessar" nao duplica trabalho.
/// </remarks>
public sealed partial class ReprocessamentoService(
    IEventoWebhookRepository eventos,
    IDeadLetterRepository deadLetters,
    IOutboxRepository outbox,
    IUnitOfWork unitOfWork,
    ILogger<ReprocessamentoService> logger) : IReprocessamentoService
{
    public async Task<ReprocessamentoAceito> ReprocessarAsync(
        Guid eventoId,
        string usuario,
        CancellationToken cancellationToken = default)
    {
        var evento = await eventos.ObterPorIdAsync(eventoId, cancellationToken)
            ?? throw new NotFoundException("Evento", eventoId);

        if (evento.Status == StatusProcessamento.Invalido)
        {
            throw new DomainException(
                "Evento reprovado na validacao nao pode ser reprocessado: o payload gravado daria o mesmo erro.");
        }

        evento.ReabrirParaReprocessamento();

        var carta = await deadLetters.ObterPorEventoAsync(eventoId, cancellationToken);
        carta?.MarcarReprocessada(usuario);

        var (traceParent, traceState) = Telemetria.ContextoAtual();
        await outbox.AdicionarAsync(
            MensagemOutbox.ParaEventoRecebido(evento.Id, traceParent, traceState),
            cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogReprocessado(logger, evento.IdTransacao, usuario);

        return new ReprocessamentoAceito(
            evento.Id,
            evento.IdTransacao,
            "Evento devolvido para a fila com um orcamento novo de tentativas.");
    }

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning,
        Message = "Evento reprocessado manualmente a partir da DLQ. id_transacao={IdTransacao} usuario={Usuario}")]
    private static partial void LogReprocessado(ILogger logger, string idTransacao, string usuario);
}
