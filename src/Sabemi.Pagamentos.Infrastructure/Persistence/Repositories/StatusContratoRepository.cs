using Microsoft.EntityFrameworkCore;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Contratos;

namespace Sabemi.Pagamentos.Infrastructure.Persistence.Repositories;

public sealed class StatusContratoRepository(AppDbContext context) : IStatusContratoRepository
{
    public Task<StatusContrato?> ObterPorContratoAsync(string idContrato, CancellationToken cancellationToken = default)
    {
        var chave = (idContrato ?? string.Empty).Trim();

        return context.StatusContratos.FirstOrDefaultAsync(c => c.IdContrato == chave, cancellationToken);
    }

    public async Task AdicionarAsync(StatusContrato contrato, CancellationToken cancellationToken = default)
        => await context.StatusContratos.AddAsync(contrato, cancellationToken);

    public async Task<PagedResult<StatusContrato>> BuscarAsync(
        string? idContrato,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default)
    {
        var consulta = context.StatusContratos.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(idContrato))
        {
            var filtro = idContrato.Trim();
            consulta = consulta.Where(c => c.IdContrato.Contains(filtro));
        }

        var total = await consulta.CountAsync(cancellationToken);

        var itens = await consulta
            .OrderByDescending(c => c.AtualizadoEmUtc)
            .ThenBy(c => c.Id)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .ToListAsync(cancellationToken);

        return new PagedResult<StatusContrato>(itens, pagina, tamanhoPagina, total);
    }
}
