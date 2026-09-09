using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Domain.Eventos;

public interface IDeadLetterRepository
{
    Task AdicionarAsync(DeadLetter carta, CancellationToken cancellationToken = default);

    Task<DeadLetter?> ObterPorEventoAsync(Guid eventoId, CancellationToken cancellationToken = default);

    Task<PagedResult<DeadLetter>> BuscarAsync(
        bool apenasPendentes,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default);

    Task<int> ContarPendentesAsync(CancellationToken cancellationToken = default);
}
