using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.Infrastructure.Persistence.Configurations;

public sealed class DeadLetterConfiguration : IEntityTypeConfiguration<DeadLetter>
{
    public void Configure(EntityTypeBuilder<DeadLetter> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DeadLetters");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.EventoId).IsRequired();
        builder.Property(c => c.IdTransacao).IsRequired().HasMaxLength(EventoWebhook.TamanhoMaximoIdTransacao);
        builder.Property(c => c.IdContrato).HasMaxLength(EventoWebhook.TamanhoMaximoIdContrato);
        builder.Property(c => c.Motivo).IsRequired().HasMaxLength(1000);
        builder.Property(c => c.CriadoEmUtc).IsRequired();
        builder.Property(c => c.ReprocessadoPor).HasMaxLength(100);

        builder.Ignore(c => c.Pendente);

        // O indice por evento nao e unico de proposito: um evento reprocessado a
        // mao que volta a falhar merece uma carta nova, e nao a sobrescrita da
        // anterior. Cada rodada de falha e um fato historico separado.
        builder.HasIndex(c => c.EventoId).HasDatabaseName("IX_DeadLetters_EventoId");
        builder.HasIndex(c => c.ReprocessadoEmUtc).HasDatabaseName("IX_DeadLetters_ReprocessadoEmUtc");
    }
}
