using Microsoft.EntityFrameworkCore;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.Infrastructure.Persistence.Repositories;

public sealed class EventoWebhookRepository(AppDbContext context) : IEventoWebhookRepository
{
    public Task<EventoWebhook?> ObterPorIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.EventosWebhook.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<EventoWebhook?> ObterPorTransacaoAsync(string idTransacao, CancellationToken cancellationToken = default)
    {
        var chave = (idTransacao ?? string.Empty).Trim();

        return context.EventosWebhook
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.IdTransacao == chave, cancellationToken);
    }

    public Task<bool> TransacaoJaRecebidaAsync(string idTransacao, CancellationToken cancellationToken = default)
    {
        var chave = (idTransacao ?? string.Empty).Trim();

        return context.EventosWebhook.AnyAsync(e => e.IdTransacao == chave, cancellationToken);
    }

    public async Task AdicionarAsync(EventoWebhook evento, CancellationToken cancellationToken = default)
        => await context.EventosWebhook.AddAsync(evento, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ObterPendentesAsync(int limite, CancellationToken cancellationToken = default)
        => await context.EventosWebhook
            .AsNoTracking()
            .Where(e => e.Status == StatusProcessamento.Recebido || e.Status == StatusProcessamento.Processando)
            .OrderBy(e => e.RecebidoEmUtc)
            .Take(limite)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<EventoWebhook>> BuscarAsync(
        ResultadoEvento? resultado,
        string? idContrato,
        string? idTransacao,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default)
    {
        var consulta = context.EventosWebhook.AsNoTracking().AsQueryable();

        if (resultado is not null)
        {
            var status = resultado.Value.Membros();
            consulta = consulta.Where(e => status.Contains(e.Status));
        }

        if (!string.IsNullOrWhiteSpace(idContrato))
        {
            var filtro = idContrato.Trim();
            consulta = consulta.Where(e => e.IdContrato != null && e.IdContrato.Contains(filtro));
        }

        if (!string.IsNullOrWhiteSpace(idTransacao))
        {
            var filtro = idTransacao.Trim();
            consulta = consulta.Where(e => e.IdTransacao.Contains(filtro));
        }

        var total = await consulta.CountAsync(cancellationToken);

        var itens = await consulta
            .OrderByDescending(e => e.RecebidoEmUtc)
            .ThenByDescending(e => e.Id)
            .Skip((pagina - 1) * tamanhoPagina)
            .Take(tamanhoPagina)
            .ToListAsync(cancellationToken);

        return new PagedResult<EventoWebhook>(itens, pagina, tamanhoPagina, total);
    }

    public async Task<IReadOnlyDictionary<StatusProcessamento, int>> ContarPorStatusAsync(
        CancellationToken cancellationToken = default)
    {
        // Agregacao no banco: o painel nunca traz a tabela inteira para contar.
        var contagens = await context.EventosWebhook
            .AsNoTracking()
            .GroupBy(e => e.Status)
            .Select(grupo => new { Status = grupo.Key, Total = grupo.Count() })
            .ToListAsync(cancellationToken);

        return contagens.ToDictionary(item => item.Status, item => item.Total);
    }
}
