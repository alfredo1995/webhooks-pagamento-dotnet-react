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
    private static readonly DateTime Agora = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IEventoWebhookRepository> _eventos = new();
    private readonly Mock<IStatusContratoRepository> _contratos = new();
    private readonly Mock<IDeadLetterRepository> _deadLetters = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly RelogioFake _relogio = new(Agora);

    private readonly OpcoesProcessamento _opcoes = new()
    {
        AtrasoSimuladoMs = 0,
        LeaseProcessamentoSegundos = 120,
        Retentativa = { MaximoTentativas = 3, BackoffBaseSegundos = 5, Fator = 2, JitterPercentual = 0 },
    };

    private ProcessadorPagamento CriarProcessador() => new(
        _eventos.Object,
        _contratos.Object,
        _deadLetters.Object,
        _unitOfWork.Object,
        new PoliticaRetentativaExponencial(Options.Create(_opcoes)),
        Options.Create(_opcoes),
        _relogio,
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

    /// <summary>
    /// Dubla a reivindicacao com a mesma regra do repositorio: so devolve o
    /// evento quando ele esta mesmo reivindicavel, e aplica a transicao do
    /// dominio. Um dublê que sempre devolvesse o evento esconderia justamente o
    /// caso que o metodo existe para tratar.
    /// </summary>
    private void RegistrarEvento(EventoWebhook evento)
        => _eventos
            .Setup(r => r.ReivindicarAsync(
                evento.Id,
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, DateTime agora, TimeSpan lease, CancellationToken _) =>
            {
                if (!evento.PodeSerReivindicado(agora, lease))
                {
                    return null;
                }

                evento.Reivindicar(agora, lease);

                return evento;
            });

    private Task ProcessarAsync(EventoWebhook evento)
        => CriarProcessador().ProcessarAsync(new MensagemProcessamento(evento.Id));

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

        await ProcessarAsync(evento);

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

        await ProcessarAsync(evento);

        contrato.ValorTotalPago.Should().Be(100m);
        _contratos.Verify(
            r => r.AdicionarAsync(It.IsAny<StatusContrato>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessarAsync_QuandoARegraDeNegocioFalha_AgendaRetentativaComBackoff()
    {
        // Estorno maior que o saldo: o agregado recusa e o evento volta agendado.
        var evento = EventoValido(valor: 500m, status: StatusPagamento.Estornado);
        RegistrarEvento(evento);

        _contratos
            .Setup(r => r.ObterPorContratoAsync("CT-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatusContrato.Novo("CT-1"));

        await ProcessarAsync(evento);

        evento.Status.Should().Be(StatusProcessamento.AguardandoRetentativa);
        evento.MotivoFalha.Should().Contain("excede o saldo liquido");
        evento.Tentativas.Should().Be(1);
        evento.ProximaTentativaEmUtc.Should().Be(Agora.AddSeconds(5));

        _deadLetters.Verify(
            r => r.AdicionarAsync(It.IsAny<DeadLetter>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProcessarAsync_AoEsgotarAsTentativas_MarcaFalhaEAbreCartaNaDeadLetterQueue()
    {
        var evento = EventoValido(valor: 500m, status: StatusPagamento.Estornado);
        RegistrarEvento(evento);

        _contratos
            .Setup(r => r.ObterPorContratoAsync("CT-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatusContrato.Novo("CT-1"));

        DeadLetter? carta = null;
        _deadLetters
            .Setup(r => r.AdicionarAsync(It.IsAny<DeadLetter>(), It.IsAny<CancellationToken>()))
            .Callback<DeadLetter, CancellationToken>((c, _) => carta = c)
            .Returns(Task.CompletedTask);

        // Tres tentativas: as duas primeiras reagendam, a terceira esgota o orcamento.
        for (var tentativa = 1; tentativa <= _opcoes.Retentativa.MaximoTentativas; tentativa++)
        {
            await ProcessarAsync(evento);
            _relogio.Avancar(TimeSpan.FromMinutes(10));
        }

        evento.Status.Should().Be(StatusProcessamento.Falha);
        evento.Tentativas.Should().Be(3);
        evento.ProximaTentativaEmUtc.Should().BeNull();

        carta.Should().NotBeNull();
        carta!.EventoId.Should().Be(evento.Id);
        carta.Tentativas.Should().Be(3);
        carta.Motivo.Should().Contain("excede o saldo liquido");
        carta.Pendente.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessarAsync_ComEventoJaProcessado_NaoReprocessa()
    {
        var evento = EventoValido();
        RegistrarEvento(evento);

        await ProcessarAsync(evento);
        _unitOfWork.Invocations.Clear();

        // Reentrega do broker depois do sucesso: a reivindicacao recusa.
        await ProcessarAsync(evento);

        evento.Tentativas.Should().Be(1);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarAsync_QuandoOutroWorkerJaReivindicou_NaoFazNada()
    {
        _eventos
            .Setup(r => r.ReivindicarAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((EventoWebhook?)null);

        var acao = () => CriarProcessador().ProcessarAsync(new MensagemProcessamento(Guid.NewGuid()));

        await acao.Should().NotThrowAsync();

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarAsync_ComEventoSemDadosValidados_AgendaRetentativa()
    {
        var evento = EventoWebhook.Registrar("TX-9", "{}", "banco");
        RegistrarEvento(evento);

        await ProcessarAsync(evento);

        evento.Status.Should().Be(StatusProcessamento.AguardandoRetentativa);
        evento.MotivoFalha.Should().Contain("sem dados validados");
    }

    [Fact]
    public async Task ProcessarAsync_AposFalha_PermiteNovaTentativaComSucesso()
    {
        var evento = EventoValido(valor: 500m, status: StatusPagamento.Estornado);
        RegistrarEvento(evento);

        var contrato = StatusContrato.Novo("CT-1");
        _contratos
            .Setup(r => r.ObterPorContratoAsync("CT-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(contrato);

        await ProcessarAsync(evento);
        evento.Status.Should().Be(StatusProcessamento.AguardandoRetentativa);

        // A causa some — o contrato passa a ter saldo — e a retentativa vence.
        contrato.AplicarPagamento("TX-0", 500m, DateTime.UtcNow, StatusPagamento.Confirmado);
        _relogio.Avancar(TimeSpan.FromSeconds(10));

        await ProcessarAsync(evento);

        evento.Status.Should().Be(StatusProcessamento.Processado);
        evento.Tentativas.Should().Be(2);
        evento.MotivoFalha.Should().BeNull();
    }
}
