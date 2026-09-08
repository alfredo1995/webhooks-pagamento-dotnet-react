namespace Sabemi.Pagamentos.Application.Processamento;

/// <summary>
/// Porta de saida para o processamento assincrono.
/// </summary>
/// <remarks>
/// A implementacao atual e uma fila em memoria (Channel) por ser suficiente para
/// o volume do desafio e nao exigir infraestrutura extra. Como a aplicacao so
/// conhece esta interface, trocar por RabbitMQ, Service Bus ou Kafka significa
/// escrever um novo adapter — nenhum caso de uso muda.
/// </remarks>
public interface IFilaProcessamento
{
    ValueTask EnfileirarAsync(Guid eventoId, CancellationToken cancellationToken = default);

    /// <summary>Quantidade de itens aguardando retirada. Usado nas metricas do painel.</summary>
    int Aguardando { get; }
}
