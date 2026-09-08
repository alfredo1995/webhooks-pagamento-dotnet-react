using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sabemi.Pagamentos.Application.Processamento;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Contratos;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.UnitTests.Aplicacao;

public class ProcessadorPagamentoTests
{
    private readonly Mock<IEventoWebhookRepository> _eventos = new();
    private readonly Mock<IStatusContratoRepository> _contratos = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private ProcessadorPagamento CriarProcessador() => new(
        _eventos.Object,
        _contratos.Object,
        _unitOfWork.Object,
        Options.Create(new OpcoesProcessamento { AtrasoSimuladoMs = 0 }),
        NullLogger<ProcessadorPagamento>.Instance);

    private static EventoWebhook EventoValido(
        string idTransacao = "TX-1",
        string idContrato = "CT-1",
        decimal valor = 100m,
        StatusPagamento status = StatusPagamento.Confirmado)
    {
        var evento = EventoWebhook.Registrar(idTransacao, "{}", "banco");
        evento.AplicarDadosValidados(idContrato, valor, DateTime.UtcNow, status);

        return evento;
    }

    private void RegistrarEvento(EventoWebhook evento)
        => _eventos.Setup(r => r.ObterPorIdAsync(evento.Id, It.IsAny<CancellationToken>())).ReturnsAsync(evento);

    [Fact]
    public async Task ProcessarAsync_ComContratoNovo_CriaOContratoEAplicaOPagamento()
    {
        var evento = EventoValido(valor: 320.50m);
        RegistrarEvento(evento);

        StatusContrato? criado = null;
        _contratos
            .Setup(r => r.AdicionarAsync(It.IsAny<StatusContrato>(), It.IsAny<CancellationToken>()))
            .Callback<StatusContrato, CancellationToken>((c, _) => criado = c)
            .Returns(Task.CompletedTask);

        await CriarProcessador().ProcessarAsync(evento.Id);

        evento.Status.Should().Be(StatusProcessamento.Processado);
        evento.DuracaoProcessamentoMs.Should().NotBeNull();
        criado.Should().NotBeNull();
        criado!.IdContrato.Should().Be("CT-1");
        criado.ValorTotalPago.Should().Be(320.50m);
    }

    [Fact]
    public async Task ProcessarAsync_ComContratoExistente_AcumulaSemCriarOutro()
    {
        var evento = EventoValido(valor: 80m);
        RegistrarEvento(evento);

        var contrato = StatusContrato.Novo("CT-1");
        contrato.AplicarPagamento("TX-0", 20m, DateTime.UtcNow, StatusPagamento.Confirmado);
        _contratos
            .Setup(r => r.ObterPorContratoAsync("CT-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(contrato);

        await CriarProcessador().ProcessarAsync(evento.Id);

        contrato.ValorTotalPago.Should().Be(100m);
        _contratos.Verify(
            r => r.AdicionarAsync(It.IsAny<StatusContrato>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessarAsync_QuandoARegraDeNegocioFalha_MarcaFalhaComMotivo()
    {
        // Estorno maior que o saldo: o agregado recusa e o evento fica com o motivo.
        var evento = EventoValido(valor: 500m, status: StatusPagamento.Estornado);
        RegistrarEvento(evento);

        var contrato = StatusContrato.Novo("CT-1");
        _contratos
            .Setup(r => r.ObterPorContratoAsync("CT-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(contrato);

        await CriarProcessador().ProcessarAsync(evento.Id);

        evento.Status.Should().Be(StatusProcessamento.Falha);
        evento.MotivoFalha.Should().Contain("excede o saldo liquido");
        evento.Tentativas.Should().Be(1);
    }

    [Fact]
    public async Task ProcessarAsync_ComEventoJaProcessado_NaoReprocessa()
    {
        var evento = EventoValido();
        evento.IniciarProcessamento();
        evento.ConcluirComSucesso(10);
        RegistrarEvento(evento);

        await CriarProcessador().ProcessarAsync(evento.Id);

        evento.Tentativas.Should().Be(1);
        _contratos.Verify(
            r => r.ObterPorContratoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarAsync_ComEventoInexistente_NaoQuebra()
    {
        _eventos
            .Setup(r => r.ObterPorIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EventoWebhook?)null);

        var acao = () => CriarProcessador().ProcessarAsync(Guid.NewGuid());

        await acao.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProcessarAsync_ComEventoSemDadosValidados_MarcaFalha()
    {
        var evento = EventoWebhook.Registrar("TX-9", "{}", "banco");
        RegistrarEvento(evento);

        await CriarProcessador().ProcessarAsync(evento.Id);

        evento.Status.Should().Be(StatusProcessamento.Falha);
        evento.MotivoFalha.Should().Contain("sem dados validados");
    }

    [Fact]
    public async Task ProcessarAsync_AposFalha_PermiteNovaTentativaComSucesso()
    {
        var evento = EventoValido();
        evento.IniciarProcessamento();
        evento.RegistrarFalha("indisponibilidade temporaria", 50);
        RegistrarEvento(evento);

        await CriarProcessador().ProcessarAsync(evento.Id);

        evento.Status.Should().Be(StatusProcessamento.Processado);
        evento.Tentativas.Should().Be(2);
        evento.MotivoFalha.Should().BeNull();
    }
}
