using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Domain.Eventos;

public interface IEventoWebhookRepository
{
    Task<EventoWebhook?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<EventoWebhook?> ObterPorTransacaoAsync(string idTransacao, CancellationToken cancellationToken = default);

    Task<bool> TransacaoJaRecebidaAsync(string idTransacao, CancellationToken cancellationToken = default);

    Task AdicionarAsync(EventoWebhook evento, CancellationToken cancellationToken = default);

    /// <summary>Eventos que ficaram no meio do caminho — usados na recuperacao pos-restart.</summary>
    Task<IReadOnlyList<Guid>> ObterPendentesAsync(int limite, CancellationToken cancellationToken = default);

    Task<PagedResult<EventoWebhook>> BuscarAsync(
        ResultadoEvento? resultado,
        string? idContrato,
        string? idTransacao,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<StatusProcessamento, int>> ContarPorStatusAsync(CancellationToken cancellationToken = default);
}
