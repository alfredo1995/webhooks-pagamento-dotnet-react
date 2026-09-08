using Microsoft.EntityFrameworkCore;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Eventos;
using Sabemi.Pagamentos.Infrastructure.Persistence.Configurations;

namespace Sabemi.Pagamentos.Infrastructure.Persistence;

/// <summary>
/// Expoe o DbContext pela interface do dominio e traduz a violacao do indice
/// unico de <c>IdTransacao</c> em <see cref="TransacaoDuplicadaException"/>.
/// </summary>
/// <remarks>
/// A traducao acontece aqui, e nao na camada de aplicacao, porque o formato do
/// erro depende do provider: SQL Server devolve os codigos 2601/2627, SQLite
/// devolve "UNIQUE constraint failed". Deixar isso vazar para o caso de uso
/// amarraria a regra de idempotencia a um banco especifico.
/// </remarks>
public sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (EhViolacaoDeUnicidade(excecao))
        {
            var duplicado = excecao.Entries
                .Select(entrada => entrada.Entity)
                .OfType<EventoWebhook>()
                .FirstOrDefault();

            // O contexto fica com a entidade recusada pendente; sem soltar,
            // qualquer consulta seguinte tentaria grava-la de novo.
            foreach (var entrada in excecao.Entries)
            {
                entrada.State = EntityState.Detached;
            }

            throw new TransacaoDuplicadaException(duplicado?.IdTransacao ?? "desconhecida", excecao);
        }
    }

    private static bool EhViolacaoDeUnicidade(DbUpdateException excecao)
    {
        var mensagem = excecao.InnerException?.Message ?? excecao.Message;

        return mensagem.Contains(EventoWebhookConfiguration.IndiceTransacao, StringComparison.OrdinalIgnoreCase)
            || mensagem.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || mensagem.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }
}
