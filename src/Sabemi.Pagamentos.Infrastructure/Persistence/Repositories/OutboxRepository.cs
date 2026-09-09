using Microsoft.EntityFrameworkCore;
using Sabemi.Pagamentos.Domain.Outbox;

namespace Sabemi.Pagamentos.Infrastructure.Persistence.Repositories;

public sealed class OutboxRepository(AppDbContext context) : IOutboxRepository
{
    public async Task AdicionarAsync(MensagemOutbox mensagem, CancellationToken cancellationToken = default)
        => await context.MensagensOutbox.AddAsync(mensagem, cancellationToken);

    /// <summary>
    /// Reserva em duas etapas: seleciona candidatas, carimba o token com um
    /// UPDATE condicional e devolve so o que o carimbo pegou.
    /// </summary>
    /// <remarks>
    /// A leitura inicial e otimista e pode competir com outro publicador; quem
    /// decide e o UPDATE, que so altera linha ainda livre. Por isso a lista final
    /// vem de um SELECT pelo token, e nao da lista de candidatas: e a unica que
    /// reflete o que este publicador realmente ganhou.
    /// </remarks>
    public async Task<IReadOnlyList<MensagemOutbox>> ReservarLoteAsync(
        string token,
        DateTime agoraUtc,
        TimeSpan duracaoReserva,
        int limite,
        CancellationToken cancellationToken = default)
    {
        var candidatas = await context.MensagensOutbox
            .AsNoTracking()
            .Where(m => m.PublicadaEmUtc == null
                        && m.ProximaTentativaEmUtc <= agoraUtc
                        && (m.ReservadaAteUtc == null || m.ReservadaAteUtc <= agoraUtc))
            .OrderBy(m => m.CriadaEmUtc)
            .Take(limite)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);

        if (candidatas.Count == 0)
        {
            return [];
        }

        var reservadaAte = agoraUtc.Add(duracaoReserva);

        var reservadas = await context.MensagensOutbox
            .Where(m => candidatas.Contains(m.Id)
                        && m.PublicadaEmUtc == null
                        && (m.ReservadaAteUtc == null || m.ReservadaAteUtc <= agoraUtc))
            .ExecuteUpdateAsync(
                atualizacao => atualizacao
                    .SetProperty(m => m.ReservaToken, token)
                    .SetProperty(m => m.ReservadaAteUtc, reservadaAte),
                cancellationToken);

        if (reservadas == 0)
        {
            return [];
        }

        // Rastreadas: o publicador chama MarcarPublicada e salva pela unidade de trabalho.
        return await context.MensagensOutbox
            .Where(m => m.ReservaToken == token && m.PublicadaEmUtc == null)
            .OrderBy(m => m.CriadaEmUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<int> ContarPendentesAsync(CancellationToken cancellationToken = default)
        => context.MensagensOutbox.AsNoTracking().CountAsync(m => m.PublicadaEmUtc == null, cancellationToken);

    public Task<int> RemoverPublicadasAsync(DateTime anterioresAUtc, CancellationToken cancellationToken = default)
        => context.MensagensOutbox
            .Where(m => m.PublicadaEmUtc != null && m.PublicadaEmUtc < anterioresAUtc)
            .ExecuteDeleteAsync(cancellationToken);
}
