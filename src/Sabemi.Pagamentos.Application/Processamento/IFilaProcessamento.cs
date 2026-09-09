namespace Sabemi.Pagamentos.Application.Processamento;

/// <summary>
/// Mensagem que trafega na fila.
/// </summary>
/// <remarks>
/// Carrega o identificador do evento e o contexto de trace, nunca o payload: o
/// dado ja esta no banco antes de a mensagem existir. Isso mantem a fila leve e,
/// principalmente, faz do banco a fonte da verdade.
/// </remarks>
public sealed record MensagemProcessamento(Guid EventoId, string? TraceParent = null, string? TraceState = null);

/// <summary>
/// Porta de saida para o processamento assincrono.
/// </summary>
/// <remarks>
/// Quem escreve aqui e o publicador do outbox, nunca o recebimento do webhook —
/// e essa separacao que garante que uma mensagem so chegue a fila depois de o
/// evento estar commitado. Ha dois adapters: um <c>Channel</c> em processo e um
/// RabbitMQ para quando existe mais de uma instancia da API. Nenhum caso de uso
/// conhece a diferenca.
/// </remarks>
public interface IFilaProcessamento
{
    ValueTask PublicarAsync(MensagemProcessamento mensagem, CancellationToken cancellationToken = default);

    /// <summary>Quantidade de mensagens aguardando consumo. Usada nas metricas do painel.</summary>
    int Aguardando { get; }
}
