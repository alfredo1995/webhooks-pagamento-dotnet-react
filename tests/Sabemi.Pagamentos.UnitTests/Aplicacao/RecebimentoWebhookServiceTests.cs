using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sabemi.Pagamentos.Application.Webhooks;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Eventos;
using Sabemi.Pagamentos.Domain.Outbox;

namespace Sabemi.Pagamentos.UnitTests.Aplicacao;

/// <summary>
/// O coracao do desafio: nenhuma transacao pode ser processada duas vezes, e um
/// payload invalido precisa continuar visivel no painel em vez de sumir.
/// </summary>
public class RecebimentoWebhookServiceTests
{
    private readonly Mock<IEventoWebhookRepository> _eventos = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IOutboxRepository> _outbox = new();

    private RecebimentoWebhookService CriarServico() => new(
        _eventos.Object,
        _outbox.Object,
        _unitOfWork.Object,
        new PagamentoWebhookValidator(),
        NullLogger<RecebimentoWebhookService>.Instance);

    private static PagamentoWebhookRequest PayloadValido(string idTransacao = "TX-1") => new()
    {
        IdTransacao = idTransacao,
        IdContrato = "CT-99",
        Valor = 250.00m,
        DataPagamento = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
        Status = "CONFIRMADO",
    };

    private const string Bruto = """{"id_transacao":"TX-1","id_contrato":"CT-99","valor":250.00,"status":"CONFIRMADO"}""";

    [Fact]
    public async Task ReceberAsync_ComPayloadValido_AceitaGravaEAnunciaNoOutbox()
    {
        var resposta = await CriarServico().ReceberAsync(PayloadValido(), Bruto, "banco-parceiro");

        resposta.Resultado.Should().Be(ResultadoRecebimento.Aceito);
        resposta.IdTransacao.Should().Be("TX-1");

        _eventos.Verify(r => r.AdicionarAsync(It.IsAny<EventoWebhook>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(
            o => o.AdicionarAsync(It.Is<MensagemOutbox>(m => m.EventoId == resposta.EventoId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReceberAsync_ComTransacaoJaRecebida_NaoGravaNemAnuncia()
    {
        var existente = EventoWebhook.Registrar("TX-1", Bruto, "banco-parceiro");
        _eventos
            .Setup(r => r.ObterPorTransacaoAsync("TX-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existente);

        var resposta = await CriarServico().ReceberAsync(PayloadValido(), Bruto, "banco-parceiro");

        resposta.Resultado.Should().Be(ResultadoRecebimento.Duplicado);
        resposta.EventoId.Should().Be(existente.Id);

        _eventos.Verify(r => r.AdicionarAsync(It.IsAny<EventoWebhook>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _outbox.Verify(
            o => o.AdicionarAsync(It.IsAny<MensagemOutbox>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReceberAsync_QuandoOIndiceUnicoAcusaCorrida_TrataComoDuplicado()
    {
        // Duas entregas simultaneas passam pela consulta antes de qualquer uma
        // gravar: quem perde a corrida recebe a violacao do indice unico.
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransacaoDuplicadaException("TX-1"));

        var vencedor = EventoWebhook.Registrar("TX-1", Bruto, "banco-parceiro");
        _eventos
            .SetupSequence(r => r.ObterPorTransacaoAsync("TX-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EventoWebhook?)null)
            .ReturnsAsync(vencedor);

        var resposta = await CriarServico().ReceberAsync(PayloadValido(), Bruto, "banco-parceiro");

        resposta.Resultado.Should().Be(ResultadoRecebimento.Duplicado);
        resposta.EventoId.Should().Be(vencedor.Id);

        // O anuncio foi preparado junto do evento, e nao depois dele: como os
        // dois estao na mesma transacao, a recusa do indice unico desfaz o par.
        // E exatamente isso que o outbox compra — nunca existe mensagem na fila
        // para um evento que o banco nao aceitou.
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReceberAsync_ComPayloadInvalido_GravaOEventoComoInvalidoENaoAnuncia()
    {
        EventoWebhook? gravado = null;
        _eventos
            .Setup(r => r.AdicionarAsync(It.IsAny<EventoWebhook>(), It.IsAny<CancellationToken>()))
            .Callback<EventoWebhook, CancellationToken>((e, _) => gravado = e)
            .Returns(Task.CompletedTask);

        var invalido = new PagamentoWebhookRequest
        {
            IdTransacao = "TX-1",
            IdContrato = null,
            Valor = -5m,
            DataPagamento = null,
            Status = "INEXISTENTE",
        };

        var resposta = await CriarServico().ReceberAsync(invalido, Bruto, "banco-parceiro");

        resposta.Resultado.Should().Be(ResultadoRecebimento.Invalido);
        resposta.Erros.Should().NotBeNull();
        resposta.Erros!.Keys.Should().Contain(["IdContrato", "Valor", "DataPagamento", "Status"]);

        gravado.Should().NotBeNull();
        gravado!.Status.Should().Be(StatusProcessamento.Invalido);
        gravado.PayloadBruto.Should().Be(Bruto);

        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(
            o => o.AdicionarAsync(It.IsAny<MensagemOutbox>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReceberAsync_ComCorpoNaoDesserializavel_RegistraEventoInvalido()
    {
        EventoWebhook? gravado = null;
        _eventos
            .Setup(r => r.AdicionarAsync(It.IsAny<EventoWebhook>(), It.IsAny<CancellationToken>()))
            .Callback<EventoWebhook, CancellationToken>((e, _) => gravado = e)
            .Returns(Task.CompletedTask);

        var resposta = await CriarServico().ReceberAsync(null, "isso nao e json", "banco-parceiro");

        resposta.Resultado.Should().Be(ResultadoRecebimento.Invalido);
        gravado!.IdTransacao.Should().StartWith("SEM-ID-");
        gravado.PayloadBruto.Should().Be("isso nao e json");
    }

    [Fact]
    public async Task ReceberAsync_SemIdTransacao_NaoConsultaDuplicidadeEUsaChaveSintetica()
    {
        EventoWebhook? gravado = null;
        _eventos
            .Setup(r => r.AdicionarAsync(It.IsAny<EventoWebhook>(), It.IsAny<CancellationToken>()))
            .Callback<EventoWebhook, CancellationToken>((e, _) => gravado = e)
            .Returns(Task.CompletedTask);

        var semId = new PagamentoWebhookRequest
        {
            IdTransacao = null,
            IdContrato = "CT-99",
            Valor = 10m,
            DataPagamento = DateTime.UtcNow,
            Status = "CONFIRMADO",
        };

        var resposta = await CriarServico().ReceberAsync(semId, Bruto, "banco-parceiro");

        resposta.Resultado.Should().Be(ResultadoRecebimento.Invalido);
        gravado!.IdTransacao.Should().StartWith("SEM-ID-");
        _eventos.Verify(
            r => r.ObterPorTransacaoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
