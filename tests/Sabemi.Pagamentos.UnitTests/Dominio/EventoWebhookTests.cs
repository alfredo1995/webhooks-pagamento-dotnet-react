using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.UnitTests.Dominio;

public class EventoWebhookTests
{
    private const string Payload = """{"id_transacao":"TX-1"}""";

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private static EventoWebhook NovoEvento() => EventoWebhook.Registrar("TX-1", Payload, "banco-parceiro");

    private static EventoWebhook EmProcessamento(DateTime? agora = null)
    {
        var evento = NovoEvento();
        evento.Reivindicar(agora ?? DateTime.UtcNow, Lease);

        return evento;
    }

    [Fact]
    public void Registrar_GuardaPayloadBrutoIntactoENasceComoRecebido()
    {
        var evento = NovoEvento();

        evento.PayloadBruto.Should().Be(Payload);
        evento.Status.Should().Be(StatusProcessamento.Recebido);
        evento.Resultado().Should().Be(ResultadoEvento.Pendente);
        evento.Tentativas.Should().Be(0);
        evento.Id.Should().NotBe(Guid.Empty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Registrar_SemIdTransacao_LancaDomainException(string? idTransacao)
    {
        var acao = () => EventoWebhook.Registrar(idTransacao, Payload, "banco");

        acao.Should().Throw<DomainException>().WithMessage("*id_transacao*");
    }

    [Fact]
    public void Registrar_IdTransacaoAcimaDoLimite_LancaDomainException()
    {
        var acao = () => EventoWebhook.Registrar(new string('x', 101), Payload, "banco");

        acao.Should().Throw<DomainException>().WithMessage("*no maximo*");
    }

    [Fact]
    public void Registrar_SemParceiro_UsaDesconhecido()
    {
        EventoWebhook.Registrar("TX-1", Payload, null).OrigemParceiro.Should().Be("desconhecido");
    }

    [Fact]
    public void MarcarInvalido_RegistraMotivoEEncerraOEvento()
    {
        var evento = NovoEvento();

        evento.MarcarInvalido("valor e obrigatorio.");

        evento.Status.Should().Be(StatusProcessamento.Invalido);
        evento.Resultado().Should().Be(ResultadoEvento.Erro);
        evento.MotivoFalha.Should().Be("valor e obrigatorio.");
        evento.Concluido.Should().BeTrue();
    }

    [Fact]
    public void MarcarInvalido_ComMotivoMuitoLongo_Trunca()
    {
        var evento = NovoEvento();

        evento.MarcarInvalido(new string('e', 5000));

        evento.MotivoFalha!.Length.Should().Be(1000);
    }

    [Fact]
    public void CicloFeliz_ReivindicarEConcluir_ContaTentativaERegistraDuracao()
    {
        var evento = EmProcessamento();

        evento.Status.Should().Be(StatusProcessamento.Processando);
        evento.Tentativas.Should().Be(1);

        evento.ConcluirComSucesso(2013);

        evento.Status.Should().Be(StatusProcessamento.Processado);
        evento.Resultado().Should().Be(ResultadoEvento.Sucesso);
        evento.DuracaoProcessamentoMs.Should().Be(2013);
        evento.ProcessadoEmUtc.Should().NotBeNull();
        evento.ReivindicadoEmUtc.Should().BeNull();
    }

    [Fact]
    public void ConcluirComSucesso_SemEstarProcessando_LancaDomainException()
    {
        var evento = NovoEvento();

        var acao = () => evento.ConcluirComSucesso(10);

        acao.Should().Throw<DomainException>().WithMessage("*em processamento*");
    }

    [Fact]
    public void Reivindicar_ComEventoInvalido_LancaDomainException()
    {
        var evento = NovoEvento();
        evento.MarcarInvalido("payload ruim");

        var acao = () => evento.Reivindicar(DateTime.UtcNow, Lease);

        acao.Should().Throw<DomainException>();
    }

    [Fact]
    public void AgendarRetentativa_DevolveOEventoAFilaNoHorarioCombinado()
    {
        var agora = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var evento = EmProcessamento(agora);

        evento.AgendarRetentativa("timeout no parceiro", 900, agora.AddSeconds(5));

        evento.Status.Should().Be(StatusProcessamento.AguardandoRetentativa);
        evento.ProximaTentativaEmUtc.Should().Be(agora.AddSeconds(5));
        evento.Concluido.Should().BeFalse();

        // Evento em retentativa e pendente, nao erro: ele ainda pode terminar bem.
        evento.Resultado().Should().Be(ResultadoEvento.Pendente);
    }

    [Fact]
    public void PodeSerReivindicado_AntesDoHorarioDaRetentativa_Recusa()
    {
        var agora = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var evento = EmProcessamento(agora);
        evento.AgendarRetentativa("falhou", 10, agora.AddSeconds(30));

        evento.PodeSerReivindicado(agora.AddSeconds(29), Lease).Should().BeFalse();
        evento.PodeSerReivindicado(agora.AddSeconds(30), Lease).Should().BeTrue();
    }

    [Fact]
    public void PodeSerReivindicado_ComLeaseVencido_DevolveOEventoTravadoEmProcessamento()
    {
        var agora = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var evento = EmProcessamento(agora);

        // A instancia que reivindicou caiu: enquanto o lease vale, ninguem mexe.
        evento.PodeSerReivindicado(agora.Add(Lease).AddSeconds(-1), Lease).Should().BeFalse();
        evento.PodeSerReivindicado(agora.Add(Lease), Lease).Should().BeTrue();
    }

    [Fact]
    public void RegistrarFalha_EncerraOEventoComoErro()
    {
        var evento = EmProcessamento();

        evento.RegistrarFalha("indisponibilidade permanente", 900);

        evento.Status.Should().Be(StatusProcessamento.Falha);
        evento.Resultado().Should().Be(ResultadoEvento.Erro);
        evento.Concluido.Should().BeTrue();
        evento.ProximaTentativaEmUtc.Should().BeNull();
    }

    [Fact]
    public void ReabrirParaReprocessamento_ZeraOOrcamentoDeTentativas()
    {
        var evento = EmProcessamento();
        evento.RegistrarFalha("quebrou", 10);

        evento.ReabrirParaReprocessamento();

        evento.Status.Should().Be(StatusProcessamento.Recebido);
        evento.Tentativas.Should().Be(0);
        evento.MotivoFalha.Should().BeNull();
        evento.ProcessadoEmUtc.Should().BeNull();
    }

    [Fact]
    public void ReabrirParaReprocessamento_ComEventoQueNaoFalhou_LancaDomainException()
    {
        var evento = NovoEvento();

        var acao = evento.ReabrirParaReprocessamento;

        acao.Should().Throw<DomainException>().WithMessage("*Falha*");
    }

    [Fact]
    public void AplicarDadosValidados_ArredondaValorParaDuasCasas()
    {
        var evento = NovoEvento();

        evento.AplicarDadosValidados("CT-1", 10.999m, DateTime.UtcNow, StatusPagamento.Confirmado);

        evento.Valor.Should().Be(11.00m);
        evento.IdContrato.Should().Be("CT-1");
    }

    [Fact]
    public void MarcarInvalido_ComEventoJaConcluido_LancaDomainException()
    {
        var evento = NovoEvento();
        evento.MarcarInvalido("primeiro motivo");

        var acao = () => evento.MarcarInvalido("segundo motivo");

        acao.Should().Throw<DomainException>().WithMessage("*ja concluido*");
    }
}

file static class EventoExtensions
{
    public static ResultadoEvento Resultado(this EventoWebhook evento) => evento.Status.ParaResultado();
}
