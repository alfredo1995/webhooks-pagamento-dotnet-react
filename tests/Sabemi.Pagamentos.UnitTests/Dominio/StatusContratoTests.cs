using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Contratos;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.UnitTests.Dominio;

public class StatusContratoTests
{
    private static StatusContrato NovoContrato() => StatusContrato.Novo("CT-0001");

    [Fact]
    public void Novo_SemIdContrato_LancaDomainException()
    {
        var acao = () => StatusContrato.Novo("  ");

        acao.Should().Throw<DomainException>().WithMessage("*id_contrato*");
    }

    [Fact]
    public void AplicarPagamento_Confirmado_SomaNoTotalEContaOPagamento()
    {
        var contrato = NovoContrato();

        contrato.AplicarPagamento("TX-1", 150.505m, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), StatusPagamento.Confirmado);

        contrato.ValorTotalPago.Should().Be(150.51m);
        contrato.QuantidadePagamentos.Should().Be(1);
        contrato.SaldoLiquido.Should().Be(150.51m);
        contrato.UltimoStatus.Should().Be(StatusPagamento.Confirmado);
        contrato.UltimaTransacao.Should().Be("TX-1");
        contrato.UltimoPagamentoUtc.Should().NotBeNull();
    }

    [Theory]
    [InlineData(StatusPagamento.Pendente)]
    [InlineData(StatusPagamento.Falha)]
    public void AplicarPagamento_PendenteOuFalha_NaoMovimentaSaldo(StatusPagamento status)
    {
        var contrato = NovoContrato();

        contrato.AplicarPagamento("TX-1", 500m, DateTime.UtcNow, status);

        contrato.ValorTotalPago.Should().Be(0m);
        contrato.QuantidadePagamentos.Should().Be(0);
        contrato.UltimoStatus.Should().Be(status);
        contrato.UltimoPagamentoUtc.Should().BeNull();
    }

    [Fact]
    public void AplicarPagamento_Estornado_AbateDoSaldoLiquido()
    {
        var contrato = NovoContrato();
        contrato.AplicarPagamento("TX-1", 300m, DateTime.UtcNow, StatusPagamento.Confirmado);

        contrato.AplicarPagamento("TX-2", 120m, DateTime.UtcNow, StatusPagamento.Estornado);

        contrato.ValorTotalPago.Should().Be(300m);
        contrato.ValorTotalEstornado.Should().Be(120m);
        contrato.SaldoLiquido.Should().Be(180m);
        contrato.QuantidadeEstornos.Should().Be(1);
    }

    [Fact]
    public void AplicarPagamento_EstornoAcimaDoSaldo_LancaDomainExceptionSemAlterarNada()
    {
        var contrato = NovoContrato();
        contrato.AplicarPagamento("TX-1", 100m, DateTime.UtcNow, StatusPagamento.Confirmado);

        var acao = () => contrato.AplicarPagamento("TX-2", 150m, DateTime.UtcNow, StatusPagamento.Estornado);

        acao.Should().Throw<DomainException>().WithMessage("*excede o saldo liquido*");
        contrato.ValorTotalEstornado.Should().Be(0m);
        contrato.SaldoLiquido.Should().Be(100m);
    }

    [Fact]
    public void AplicarPagamento_VariosConfirmados_Acumula()
    {
        var contrato = NovoContrato();

        contrato.AplicarPagamento("TX-1", 100m, DateTime.UtcNow, StatusPagamento.Confirmado);
        contrato.AplicarPagamento("TX-2", 250.25m, DateTime.UtcNow, StatusPagamento.Confirmado);

        contrato.ValorTotalPago.Should().Be(350.25m);
        contrato.QuantidadePagamentos.Should().Be(2);
        contrato.UltimaTransacao.Should().Be("TX-2");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void AplicarPagamento_ValorNaoPositivo_LancaDomainException(decimal valor)
    {
        var contrato = NovoContrato();

        var acao = () => contrato.AplicarPagamento("TX-1", valor, DateTime.UtcNow, StatusPagamento.Confirmado);

        acao.Should().Throw<DomainException>().WithMessage("*maior que zero*");
    }
}
