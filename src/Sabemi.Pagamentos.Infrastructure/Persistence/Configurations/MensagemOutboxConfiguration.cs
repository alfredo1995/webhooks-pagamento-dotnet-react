using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sabemi.Pagamentos.Domain.Outbox;

namespace Sabemi.Pagamentos.Infrastructure.Persistence.Configurations;

public sealed class MensagemOutboxConfiguration : IEntityTypeConfiguration<MensagemOutbox>
{
    public void Configure(EntityTypeBuilder<MensagemOutbox> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("MensagensOutbox");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Tipo).IsRequired().HasMaxLength(100);
        builder.Property(m => m.EventoId).IsRequired();
        builder.Property(m => m.TraceParent).HasMaxLength(64);
        builder.Property(m => m.TraceState).HasMaxLength(512);
        builder.Property(m => m.CriadaEmUtc).IsRequired();
        builder.Property(m => m.ProximaTentativaEmUtc).IsRequired();
        builder.Property(m => m.UltimoErro).HasMaxLength(1000);
        builder.Property(m => m.ReservaToken).HasMaxLength(64);

        builder.Ignore(m => m.Publicada);

        // O publicador so procura mensagem nao publicada e ja elegivel: o indice
        // acompanha exatamente essa consulta, que roda a cada ciclo.
        builder.HasIndex(m => new { m.PublicadaEmUtc, m.ProximaTentativaEmUtc })
            .HasDatabaseName("IX_MensagensOutbox_Pendentes");

        builder.HasIndex(m => m.ReservaToken).HasDatabaseName("IX_MensagensOutbox_ReservaToken");
        builder.HasIndex(m => m.EventoId).HasDatabaseName("IX_MensagensOutbox_EventoId");
    }
}
