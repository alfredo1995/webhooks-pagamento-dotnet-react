using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Domain.Eventos;

public interface IEventoWebhookRepository
{
    Task<EventoWebhook?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<EventoWebhook?> ObterPorTransacaoAsync(string idTransacao, CancellationToken cancellationToken = default);

    Task<bool> TransacaoJaRecebidaAsync(string idTransacao, CancellationToken cancellationToken = default);

    Task AdicionarAsync(EventoWebhook evento, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reivindica o evento para processamento e devolve o agregado ja rastreado.
    /// </summary>
    /// <remarks>
    /// Devolve <c>null</c> quando outro worker chegou primeiro ou o evento ja
    /// terminou. A transicao e um UPDATE condicional no banco, e nao uma leitura
    /// seguida de gravacao, justamente para nao existir janela entre decidir e
    /// reivindicar quando ha mais de uma instancia consumindo a mesma fila —
    /// veja <see cref="EventoWebhook.Reivindicar"/>, que define a mesma regra.
    /// </remarks>
    /// <param name="id">Evento a reivindicar.</param>
    /// <param name="agoraUtc">Instante da reivindicacao.</param>
    /// <param name="lease">Prazo apos o qual um evento travado em processamento volta a ser reivindicavel.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<EventoWebhook?> ReivindicarAsync(
        Guid id,
        DateTime agoraUtc,
        TimeSpan lease,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Eventos que precisam voltar para a fila: retentativa vencida, lease de
    /// processamento expirado ou recebimento que nunca foi consumido.
    /// </summary>
    Task<IReadOnlyList<Guid>> ObterElegiveisParaReentregaAsync(
        DateTime agoraUtc,
        TimeSpan lease,
        int limite,
        CancellationToken cancellationToken = default);

    Task<PagedResult<EventoWebhook>> BuscarAsync(
        ResultadoEvento? resultado,
        string? idContrato,
        string? idTransacao,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<StatusProcessamento, int>> ContarPorStatusAsync(CancellationToken cancellationToken = default);
}
