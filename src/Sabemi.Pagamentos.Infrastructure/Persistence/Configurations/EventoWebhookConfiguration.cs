using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.Infrastructure.Persistence.Configurations;

public sealed class EventoWebhookConfiguration : IEntityTypeConfiguration<EventoWebhook>
{
    /// <summary>Nome do indice usado tambem para reconhecer a violacao de unicidade.</summary>
    public const string IndiceTransacao = "UX_EventosWebhook_IdTransacao";

    public void Configure(EntityTypeBuilder<EventoWebhook> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("EventosWebhook");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.IdTransacao)
            .IsRequired()
            .HasMaxLength(EventoWebhook.TamanhoMaximoIdTransacao);

        builder.Property(e => e.PayloadBruto).IsRequired();
        builder.Property(e => e.OrigemParceiro).IsRequired().HasMaxLength(100);
        builder.Property(e => e.RecebidoEmUtc).IsRequired();
        builder.Property(e => e.Status).HasConversion<int>().IsRequired();
        builder.Property(e => e.IdContrato).HasMaxLength(EventoWebhook.TamanhoMaximoIdContrato);
        builder.Property(e => e.Valor).HasPrecision(18, 2);
        builder.Property(e => e.StatusPagamento).HasConversion<int?>();
        builder.Property(e => e.MotivoFalha).HasMaxLength(1000);

        builder.Ignore(e => e.Concluido);

        // A garantia real de idempotencia: mesmo com duas requisicoes simultaneas,
        // o banco so aceita um registro por id_transacao.
        builder.HasIndex(e => e.IdTransacao).IsUnique().HasDatabaseName(IndiceTransacao);

        builder.HasIndex(e => e.Status).HasDatabaseName("IX_EventosWebhook_Status");
        builder.HasIndex(e => e.IdContrato).HasDatabaseName("IX_EventosWebhook_IdContrato");
        builder.HasIndex(e => e.RecebidoEmUtc).HasDatabaseName("IX_EventosWebhook_RecebidoEmUtc");

        // O supervisor varre por status e data de elegibilidade a cada ciclo;
        // sem este indice, cada varredura seria um scan da tabela inteira.
        builder.HasIndex(e => new { e.Status, e.ProximaTentativaEmUtc })
            .HasDatabaseName("IX_EventosWebhook_Status_ProximaTentativa");
    }
}
