using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Domain.Contratos;

public interface IStatusContratoRepository
{
    Task<StatusContrato?> ObterPorContratoAsync(string idContrato, CancellationToken cancellationToken = default);

    Task AdicionarAsync(StatusContrato contrato, CancellationToken cancellationToken = default);

    Task<PagedResult<StatusContrato>> BuscarAsync(
        string? idContrato,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default);
}
