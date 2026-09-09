using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sabemi.Pagamentos.Domain.Auditoria;

namespace Sabemi.Pagamentos.Infrastructure.Persistence.Configurations;

public sealed class RegistroAuditoriaConfiguration : IEntityTypeConfiguration<RegistroAuditoria>
{
    public void Configure(EntityTypeBuilder<RegistroAuditoria> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("RegistrosAuditoria");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Usuario).IsRequired().HasMaxLength(150);
        builder.Property(r => r.Papel).IsRequired().HasMaxLength(50);
        builder.Property(r => r.Metodo).IsRequired().HasMaxLength(10);
        builder.Property(r => r.Recurso).IsRequired().HasMaxLength(300);
        builder.Property(r => r.Consulta).HasMaxLength(RegistroAuditoria.TamanhoMaximoConsulta);
        builder.Property(r => r.IpOrigem).IsRequired().HasMaxLength(64);
        builder.Property(r => r.TraceId).HasMaxLength(64);
        builder.Property(r => r.EmUtc).IsRequired();

        builder.HasIndex(r => r.EmUtc).HasDatabaseName("IX_RegistrosAuditoria_EmUtc");
        builder.HasIndex(r => r.Usuario).HasDatabaseName("IX_RegistrosAuditoria_Usuario");
    }
}
