using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.UnitTests.Dominio;

public class DeadLetterTests
{
    private static EventoWebhook EventoQueFalhou()
    {
        var evento = EventoWebhook.Registrar("TX-DLQ", "{}", "banco-parceiro");
        evento.AplicarDadosValidados("CT-7", 10m, DateTime.UtcNow, StatusPagamento.Confirmado);
        evento.Reivindicar(DateTime.UtcNow, TimeSpan.FromMinutes(2));
        evento.RegistrarFalha("dependencia fora do ar", 120);

        return evento;
    }

    [Fact]
    public void DoEvento_CopiaOQueOOperadorPrecisaParaDecidir()
    {
        var evento = EventoQueFalhou();

        var carta = DeadLetter.DoEvento(evento);

        carta.EventoId.Should().Be(evento.Id);
        carta.IdTransacao.Should().Be("TX-DLQ");
        carta.IdContrato.Should().Be("CT-7");
        carta.Motivo.Should().Be("dependencia fora do ar");
        carta.Tentativas.Should().Be(evento.Tentativas);
        carta.Pendente.Should().BeTrue();
    }

    [Fact]
    public void DoEvento_ComEventoQueNaoFalhou_LancaDomainException()
    {
        var evento = EventoWebhook.Registrar("TX-1", "{}", "banco");

        var acao = () => DeadLetter.DoEvento(evento);

        acao.Should().Throw<DomainException>().WithMessage("*dead-letter*");
    }

    [Fact]
    public void MarcarReprocessada_GuardaQuemMandou()
    {
        var carta = DeadLetter.DoEvento(EventoQueFalhou());

        carta.MarcarReprocessada("admin");

        carta.Pendente.Should().BeFalse();
        carta.ReprocessadoPor.Should().Be("admin");
        carta.ReprocessadoEmUtc.Should().NotBeNull();
    }

    [Fact]
    public void MarcarReprocessada_DuasVezes_LancaDomainException()
    {
        var carta = DeadLetter.DoEvento(EventoQueFalhou());
        carta.MarcarReprocessada("admin");

        var acao = () => carta.MarcarReprocessada("admin");

        acao.Should().Throw<DomainException>().WithMessage("*ja foi reprocessada*");
    }
}
