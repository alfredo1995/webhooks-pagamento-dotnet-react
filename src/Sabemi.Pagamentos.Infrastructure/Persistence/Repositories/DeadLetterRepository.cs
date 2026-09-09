using Microsoft.EntityFrameworkCore;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.Infrastructure.Persistence.Repositories;

public sealed class DeadLetterRepository(AppDbContext context) : IDeadLetterRepository
{
    public async Task AdicionarAsync(DeadLetter carta, CancellationToken cancellationToken = default)
        => await context.DeadLetters.AddAsync(carta, cancellationToken);

    /// <summary>A carta pendente do evento; as ja reprocessadas ficam apenas como historico.</summary>
    public Task<DeadLetter?> ObterPorEventoAsync(Guid eventoId, CancellationToken cancellationToken = default)
        => context.DeadLetters
            .Where(c => c.EventoId == eventoId && c.ReprocessadoEmUtc == null)
            .OrderByDescending(c => c.CriadoEmUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<DeadLetter>> BuscarAsync(
        bool apenasPendentes,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default)
    {
        var consulta = context.DeadLetters.AsNoTracking().AsQueryable();

        if (apenasPendentes)
        {
            consulta = consulta.Where(c => c.ReprocessadoEmUtc == null);
        }

        var total = await consulta.CountAsync(cancellationToken);

        var itens = await consulta
            .OrderByDescending(c => c.CriadoEmUtc)
            .ThenByDescending(c => c.Id)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .ToListAsync(cancellationToken);

        return new PagedResult<DeadLetter>(itens, pagina, tamanhoPagina, total);
    }

    public Task<int> ContarPendentesAsync(CancellationToken cancellationToken = default)
        => context.DeadLetters.AsNoTracking().CountAsync(c => c.ReprocessadoEmUtc == null, cancellationToken);
}
