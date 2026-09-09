using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Domain.Auditoria;

public interface IAuditoriaRepository
{
    Task AdicionarAsync(RegistroAuditoria registro, CancellationToken cancellationToken = default);

    Task<PagedResult<RegistroAuditoria>> BuscarAsync(
        string? usuario,
        string? recurso,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default);
}
