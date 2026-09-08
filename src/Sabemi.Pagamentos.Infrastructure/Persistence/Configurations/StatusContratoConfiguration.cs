using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sabemi.Pagamentos.Domain.Contratos;

namespace Sabemi.Pagamentos.Infrastructure.Persistence.Configurations;

public sealed class StatusContratoConfiguration : IEntityTypeConfiguration<StatusContrato>
{
    public void Configure(EntityTypeBuilder<StatusContrato> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("StatusContratos");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.IdContrato).IsRequired().HasMaxLength(100);
        builder.Property(c => c.ValorTotalPago).HasPrecision(18, 2).IsRequired();
        builder.Property(c => c.ValorTotalEstornado).HasPrecision(18, 2).IsRequired();
        builder.Property(c => c.UltimoStatus).HasConversion<int>().IsRequired();
        builder.Property(c => c.UltimaTransacao).IsRequired().HasMaxLength(100);

        // Saldo e derivado dos totais: calculado no dominio, nunca persistido.
        builder.Ignore(c => c.SaldoLiquido);

        builder.HasIndex(c => c.IdContrato).IsUnique().HasDatabaseName("UX_StatusContratos_IdContrato");
    }
}
