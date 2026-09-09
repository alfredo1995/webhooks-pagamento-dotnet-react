using Microsoft.EntityFrameworkCore;
using Sabemi.Pagamentos.Domain.Auditoria;
using Sabemi.Pagamentos.Domain.Contratos;
using Sabemi.Pagamentos.Domain.Eventos;
using Sabemi.Pagamentos.Domain.Outbox;

namespace Sabemi.Pagamentos.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>Log de eventos brutos: tudo que o parceiro enviou, valido ou nao.</summary>
    public DbSet<EventoWebhook> EventosWebhook => Set<EventoWebhook>();

    /// <summary>Status consolidado por contrato.</summary>
    public DbSet<StatusContrato> StatusContratos => Set<StatusContrato>();

    /// <summary>Anuncios pendentes de publicacao na fila, gravados junto do evento.</summary>
    public DbSet<MensagemOutbox> MensagensOutbox => Set<MensagemOutbox>();

    /// <summary>Eventos que esgotaram as retentativas automaticas.</summary>
    public DbSet<DeadLetter> DeadLetters => Set<DeadLetter>();

    /// <summary>Trilha de auditoria das consultas do painel.</summary>
    public DbSet<RegistroAuditoria> RegistrosAuditoria => Set<RegistroAuditoria>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
