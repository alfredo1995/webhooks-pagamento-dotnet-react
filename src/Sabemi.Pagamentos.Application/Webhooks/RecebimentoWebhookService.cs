using FluentValidation;
using Microsoft.Extensions.Logging;
using Sabemi.Pagamentos.Application.Observabilidade;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Eventos;
using Sabemi.Pagamentos.Domain.Outbox;

namespace Sabemi.Pagamentos.Application.Webhooks;

public interface IRecebimentoWebhookService
{
    Task<RespostaRecebimento> ReceberAsync(
        PagamentoWebhookRequest? payload,
        string payloadBruto,
        string parceiro,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Recebe a notificacao do banco, garante idempotencia e devolve o controle
/// rapidamente: o trabalho pesado sai daqui pelo outbox.
/// </summary>
/// <remarks>
/// <para>
/// A idempotencia tem duas camadas. A consulta previa resolve o caso comum (o
/// banco reenviando por timeout) sem custo de excecao; o indice unico em
/// <c>IdTransacao</c> resolve o caso real de corrida, quando duas entregas da
/// mesma transacao chegam ao mesmo tempo e as duas passam pela consulta antes de
/// qualquer uma gravar. Sem o indice, a checagem sozinha seria apenas uma
/// otimizacao com aparencia de garantia.
/// </para>
/// <para>
/// O evento e a mensagem de outbox sao gravados no mesmo <c>SaveChanges</c>, ou
/// seja, na mesma transacao. Publicar direto na fila aqui abriria a fresta
/// classica: mensagem entregue e commit desfeito depois, ou commit feito e
/// publicacao perdida. Com o outbox, so existe um desfecho — os dois ou nenhum.
/// </para>
/// </remarks>
public sealed partial class RecebimentoWebhookService(
    IEventoWebhookRepository eventos,
    IOutboxRepository outbox,
    IUnitOfWork unitOfWork,
    IValidator<PagamentoWebhookRequest> validador,
    ILogger<RecebimentoWebhookService> logger) : IRecebimentoWebhookService
{
    public async Task<RespostaRecebimento> ReceberAsync(
        PagamentoWebhookRequest? payload,
        string payloadBruto,
        string parceiro,
        CancellationToken cancellationToken = default)
    {
        var idTransacao = payload?.IdTransacao?.Trim();

        // Sem chave de idempotencia nao ha como deduplicar, mas o evento ainda
        // precisa aparecer no painel: registramos sob uma chave sintetica.
        var chave = string.IsNullOrWhiteSpace(idTransacao)
            ? $"SEM-ID-{Guid.NewGuid():N}"
            : idTransacao;

        if (!string.IsNullOrWhiteSpace(idTransacao))
        {
            var existente = await eventos.ObterPorTransacaoAsync(idTransacao, cancellationToken);
            if (existente is not null)
            {
                LogDuplicado(logger, idTransacao, existente.Status.ToString());
                ContarRecebimento(parceiro, "duplicado");

                return new RespostaRecebimento(
                    ResultadoRecebimento.Duplicado,
                    existente.Id,
                    existente.IdTransacao,
                    $"Transacao ja recebida em {existente.RecebidoEmUtc:O}. Nenhum reprocessamento foi disparado.");
            }
        }

        var evento = EventoWebhook.Registrar(chave, payloadBruto, parceiro);

        var validacao = payload is null
            ? null
            : await validador.ValidateAsync(payload, cancellationToken);

        if (payload is null || !validacao!.IsValid)
        {
            var erros = payload is null
                ? new Dictionary<string, string[]> { ["payload"] = ["Corpo da requisicao vazio ou nao e um JSON valido."] }
                : validacao!.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            evento.MarcarInvalido(string.Join(" | ", erros.SelectMany(e => e.Value)));

            // Payload invalido nao gera mensagem de outbox: reprocessar um corpo
            // que ja foi reprovado pela validacao daria exatamente o mesmo erro.
            await eventos.AdicionarAsync(evento, cancellationToken);
            await PersistirAsync(cancellationToken);

            LogInvalido(logger, evento.IdTransacao, evento.MotivoFalha ?? string.Empty);
            ContarRecebimento(parceiro, "invalido");

            return new RespostaRecebimento(
                ResultadoRecebimento.Invalido,
                evento.Id,
                evento.IdTransacao,
                "Evento registrado no log, porem reprovado na validacao.",
                erros);
        }

        StatusPagamentoParser.TentarConverter(payload.Status, out var statusPagamento);
        evento.AplicarDadosValidados(
            payload.IdContrato!,
            payload.Valor!.Value,
            payload.DataPagamento!.Value.ToUniversalTime(),
            statusPagamento);

        var (traceParent, traceState) = Telemetria.ContextoAtual();

        await eventos.AdicionarAsync(evento, cancellationToken);
        await outbox.AdicionarAsync(
            MensagemOutbox.ParaEventoRecebido(evento.Id, traceParent, traceState),
            cancellationToken);

        if (!await PersistirAsync(cancellationToken))
        {
            var existente = await eventos.ObterPorTransacaoAsync(evento.IdTransacao, cancellationToken);

            LogCorridaDetectada(logger, evento.IdTransacao);
            ContarRecebimento(parceiro, "duplicado");

            return new RespostaRecebimento(
                ResultadoRecebimento.Duplicado,
                existente?.Id ?? Guid.Empty,
                evento.IdTransacao,
                "Transacao recebida simultaneamente por outra requisicao. Nenhum reprocessamento foi disparado.");
        }

        LogAceito(logger, evento.IdTransacao, evento.IdContrato ?? "-");
        ContarRecebimento(parceiro, "aceito");

        return new RespostaRecebimento(
            ResultadoRecebimento.Aceito,
            evento.Id,
            evento.IdTransacao,
            "Evento aceito e enfileirado para processamento.");
    }

    /// <summary>Grava o que estiver pendente. Devolve <c>false</c> quando o indice unico acusou duplicidade.</summary>
    private async Task<bool> PersistirAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (TransacaoDuplicadaException)
        {
            return false;
        }
    }

    private static void ContarRecebimento(string parceiro, string resultado)
        => Telemetria.EventosRecebidos.Add(
            1,
            new KeyValuePair<string, object?>("parceiro", parceiro),
            new KeyValuePair<string, object?>("resultado", resultado));

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Webhook aceito. id_transacao={IdTransacao} id_contrato={IdContrato}")]
    private static partial void LogAceito(ILogger logger, string idTransacao, string idContrato);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information,
        Message = "Webhook duplicado ignorado. id_transacao={IdTransacao} status_atual={Status}")]
    private static partial void LogDuplicado(ILogger logger, string idTransacao, string status);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning,
        Message = "Webhook invalido registrado. id_transacao={IdTransacao} motivo={Motivo}")]
    private static partial void LogInvalido(ILogger logger, string idTransacao, string motivo);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning,
        Message = "Corrida de duplicidade barrada pelo indice unico. id_transacao={IdTransacao}")]
    private static partial void LogCorridaDetectada(ILogger logger, string idTransacao);
}
