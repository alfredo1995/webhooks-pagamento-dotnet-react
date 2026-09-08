using Sabemi.Pagamentos.Application.Webhooks;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.UnitTests.Aplicacao;

public class CalculadoraAssinaturaTests
{
    private const string Segredo = "segredo-hmac-de-desenvolvimento";
    private const string Corpo = """{"id_transacao":"TX-1","valor":10.5}""";

    [Fact]
    public void Calcular_UsaOPrefixoSha256EHexadecimalMinusculo()
    {
        var assinatura = CalculadoraAssinatura.Calcular(Corpo, Segredo);

        assinatura.Should().StartWith("sha256=");
        assinatura[7..].Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Calcular_EDeterministica()
    {
        CalculadoraAssinatura.Calcular(Corpo, Segredo)
            .Should()
            .Be(CalculadoraAssinatura.Calcular(Corpo, Segredo));
    }

    [Fact]
    public void Conferir_ComAssinaturaCorreta_Aceita()
    {
        var assinatura = CalculadoraAssinatura.Calcular(Corpo, Segredo);

        CalculadoraAssinatura.Conferir(Corpo, Segredo, assinatura).Should().BeTrue();
    }

    [Fact]
    public void Conferir_ComCorpoAlterado_Recusa()
    {
        var assinatura = CalculadoraAssinatura.Calcular(Corpo, Segredo);
        var adulterado = Corpo.Replace("10.5", "9999.99", StringComparison.Ordinal);

        CalculadoraAssinatura.Conferir(adulterado, Segredo, assinatura).Should().BeFalse();
    }

    [Fact]
    public void Conferir_ComSegredoErrado_Recusa()
    {
        var assinatura = CalculadoraAssinatura.Calcular(Corpo, "outro-segredo");

        CalculadoraAssinatura.Conferir(Corpo, Segredo, assinatura).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sha256=nao-e-hash")]
    [InlineData("md5=abc")]
    public void Conferir_ComAssinaturaAusenteOuMalformada_Recusa(string? assinatura)
    {
        CalculadoraAssinatura.Conferir(Corpo, Segredo, assinatura).Should().BeFalse();
    }
}

public class PagamentoWebhookValidatorTests
{
    private readonly PagamentoWebhookValidator _validador = new();

    private static PagamentoWebhookRequest Valido() => new()
    {
        IdTransacao = "TX-1",
        IdContrato = "CT-1",
        Valor = 100m,
        DataPagamento = DateTime.UtcNow.AddMinutes(-5),
        Status = "CONFIRMADO",
    };

    [Fact]
    public void Validate_ComPayloadCompleto_EValido()
    {
        _validador.Validate(Valido()).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("confirmado")]
    [InlineData("PAGO")]
    [InlineData("Liquidado")]
    [InlineData("pendente")]
    [InlineData("ESTORNADO")]
    [InlineData("falha")]
    public void Validate_AceitaAsVariacoesDeStatusUsadasNaPratica(string status)
    {
        var payload = new PagamentoWebhookRequest
        {
            IdTransacao = "TX-1",
            IdContrato = "CT-1",
            Valor = 10m,
            DataPagamento = DateTime.UtcNow,
            Status = status,
        };

        _validador.Validate(payload).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ComStatusDesconhecido_Reprova()
    {
        var payload = new PagamentoWebhookRequest
        {
            IdTransacao = "TX-1",
            IdContrato = "CT-1",
            Valor = 10m,
            DataPagamento = DateTime.UtcNow,
            Status = "QUALQUER-COISA",
        };

        var resultado = _validador.Validate(payload);

        resultado.IsValid.Should().BeFalse();
        resultado.Errors.Should().Contain(e => e.PropertyName == "Status");
    }

    [Fact]
    public void Validate_ComDataMuitoNoFuturo_Reprova()
    {
        var payload = new PagamentoWebhookRequest
        {
            IdTransacao = "TX-1",
            IdContrato = "CT-1",
            Valor = 10m,
            DataPagamento = DateTime.UtcNow.AddDays(3),
            Status = "CONFIRMADO",
        };

        _validador.Validate(payload).Errors.Should().Contain(e => e.PropertyName == "DataPagamento");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_ComValorNaoPositivo_Reprova(decimal valor)
    {
        var payload = new PagamentoWebhookRequest
        {
            IdTransacao = "TX-1",
            IdContrato = "CT-1",
            Valor = valor,
            DataPagamento = DateTime.UtcNow,
            Status = "CONFIRMADO",
        };

        _validador.Validate(payload).Errors.Should().Contain(e => e.PropertyName == "Valor");
    }

    [Fact]
    public void Validate_SemCamposObrigatorios_AcumulaTodosOsErros()
    {
        var resultado = _validador.Validate(new PagamentoWebhookRequest());

        resultado.Errors.Select(e => e.PropertyName)
            .Should()
            .Contain(["IdTransacao", "IdContrato", "Valor", "DataPagamento", "Status"]);
    }
}

public class StatusPagamentoParserTests
{
    [Theory]
    [InlineData("CONFIRMADO", StatusPagamento.Confirmado)]
    [InlineData("pago", StatusPagamento.Confirmado)]
    [InlineData("  Liquidado  ", StatusPagamento.Confirmado)]
    [InlineData("chargeback", StatusPagamento.Estornado)]
    [InlineData("failed", StatusPagamento.Falha)]
    [InlineData("aguardando", StatusPagamento.Pendente)]
    public void TentarConverter_MapeiaSinonimos(string entrada, StatusPagamento esperado)
    {
        StatusPagamentoParser.TentarConverter(entrada, out var status).Should().BeTrue();
        status.Should().Be(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("desconhecido")]
    public void TentarConverter_ComEntradaInvalida_RetornaFalso(string? entrada)
    {
        StatusPagamentoParser.TentarConverter(entrada, out _).Should().BeFalse();
    }
}
