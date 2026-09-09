using Microsoft.EntityFrameworkCore;
using Sabemi.Pagamentos.Domain.Auditoria;
using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Infrastructure.Persistence.Repositories;

public sealed class AuditoriaRepository(AppDbContext context) : IAuditoriaRepository
{
    public async Task AdicionarAsync(RegistroAuditoria registro, CancellationToken cancellationToken = default)
        => await context.RegistrosAuditoria.AddAsync(registro, cancellationToken);

    public async Task<PagedResult<RegistroAuditoria>> BuscarAsync(
        string? usuario,
        string? recurso,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default)
    {
        var consulta = context.RegistrosAuditoria.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(usuario))
        {
            var filtro = usuario.Trim();
            consulta = consulta.Where(r => r.Usuario.Contains(filtro));
        }

        if (!string.IsNullOrWhiteSpace(recurso))
        {
            var filtro = recurso.Trim();
            consulta = consulta.Where(r => r.Recurso.Contains(filtro));
        }

        var total = await consulta.CountAsync(cancellationToken);

        var itens = await consulta
            .OrderByDescending(r => r.EmUtc)
            .ThenByDescending(r => r.Id)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .ToListAsync(cancellationToken);

        return new PagedResult<RegistroAuditoria>(itens, pagina, tamanhoPagina, total);
    }
}
