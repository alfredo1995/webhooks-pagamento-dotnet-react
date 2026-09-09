using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Application.Observabilidade;
using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Contratos;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.Application.Processamento;

public interface IProcessadorPagamento
{
    Task ProcessarAsync(MensagemProcessamento mensagem, CancellationToken cancellationToken = default);
}

/// <summary>
/// A regra de negocio "pesada" que roda fora do ciclo da requisicao HTTP.
/// </summary>
/// <remarks>
/// <para>
/// Toda entrega comeca por uma reivindicacao atomica: um UPDATE condicional que
/// so passa se o evento estiver reivindicavel. E dai que vem a idempotencia sob
/// concorrencia — reentrega do broker, retentativa agendada e duas instancias
/// consumindo a mesma fila caem todas no mesmo ponto, e apenas uma segue adiante.
/// Conferir o status em memoria antes de gravar seria uma checagem com aparencia
/// de garantia, do mesmo jeito que a consulta previa e no recebimento.
/// </para>
/// <para>
/// O desfecho de uma falha depende do orcamento de tentativas: enquanto houver,
/// o evento volta agendado com backoff; quando acaba, ele para em
/// <see cref="StatusProcessamento.Falha"/> e ganha uma carta na dead-letter queue,
/// de onde so sai por decisao humana.
/// </para>
/// </remarks>
public sealed partial class ProcessadorPagamento(
    IEventoWebhookRepository eventos,
    IStatusContratoRepository contratos,
    IDeadLetterRepository deadLetters,
    IUnitOfWork unitOfWork,
    IPoliticaRetentativa politica,
    IOptions<OpcoesProcessamento> opcoes,
    TimeProvider relogio,
    ILogger<ProcessadorPagamento> logger) : IProcessadorPagamento
{
    private readonly OpcoesProcessamento _opcoes = opcoes.Value;

    public async Task ProcessarAsync(MensagemProcessamento mensagem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mensagem);

        using var atividade = Telemetria.IniciarConsumo(
            "processar-evento",
            mensagem.TraceParent,
            mensagem.TraceState);

        atividade?.SetTag("sabemi.evento_id", mensagem.EventoId);

        var agora = relogio.GetUtcNow().UtcDateTime;
        var lease = TimeSpan.FromSeconds(Math.Max(1, _opcoes.LeaseProcessamentoSegundos));

        var evento = await eventos.ReivindicarAsync(mensagem.EventoId, agora, lease, cancellationToken);
        if (evento is null)
        {
            // Outro consumidor chegou primeiro, o evento ja terminou ou a
            // retentativa ainda nao venceu. Nos tres casos nao ha o que fazer.
            LogEntregaIgnorada(logger, mensagem.EventoId);
            atividade?.SetTag("sabemi.reivindicado", false);

            return;
        }

        atividade?.SetTag("sabemi.id_transacao", evento.IdTransacao);
        atividade?.SetTag("sabemi.tentativa", evento.Tentativas);

        var cronometro = Stopwatch.StartNew();

        try
        {
            // Simula a regra de negocio custosa exigida pelo desafio.
            if (_opcoes.AtrasoSimuladoMs > 0)
            {
                await Task.Delay(_opcoes.AtrasoSimuladoMs, cancellationToken);
            }

            await AplicarNoContratoAsync(evento, cancellationToken);

            cronometro.Stop();
            evento.ConcluirComSucesso(cronometro.ElapsedMilliseconds);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            Telemetria.EventosProcessados.Add(1, new KeyValuePair<string, object?>("parceiro", evento.OrigemParceiro));
            Telemetria.DuracaoProcessamento.Record(cronometro.Elapsed.TotalMilliseconds);

            LogProcessado(logger, evento.IdTransacao, cronometro.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Desligamento em andamento: nao marca falha. O lease vence e o
            // supervisor devolve o evento para a fila na proxima subida.
            throw;
        }
        catch (Exception excecao)
        {
            cronometro.Stop();

            await TratarFalhaAsync(evento, excecao, cronometro.ElapsedMilliseconds);

            atividade?.SetStatus(ActivityStatusCode.Error, excecao.Message);
        }
    }

    /// <summary>
    /// Falhou: agenda a proxima tentativa enquanto houver orcamento, ou encerra
    /// o evento e abre a carta na dead-letter queue quando ele acaba.
    /// </summary>
    private async Task TratarFalhaAsync(EventoWebhook evento, Exception excecao, long duracaoMs)
    {
        var etiqueta = new KeyValuePair<string, object?>("parceiro", evento.OrigemParceiro);

        if (politica.PodeRetentar(evento.Tentativas))
        {
            var proxima = politica.CalcularProximaTentativa(
                evento.Id,
                evento.Tentativas,
                relogio.GetUtcNow().UtcDateTime);

            evento.AgendarRetentativa(excecao.Message, duracaoMs, proxima);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            Telemetria.EventosRetentados.Add(1, etiqueta);
            LogRetentativaAgendada(logger, excecao, evento.IdTransacao, evento.Tentativas, proxima);

            return;
        }

        evento.RegistrarFalha(excecao.Message, duracaoMs);
        await deadLetters.AdicionarAsync(DeadLetter.DoEvento(evento), CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        Telemetria.EventosEmDeadLetter.Add(1, etiqueta);
        LogDeadLetter(logger, excecao, evento.IdTransacao, evento.Tentativas);
    }

    private async Task AplicarNoContratoAsync(EventoWebhook evento, CancellationToken cancellationToken)
    {
        if (evento.IdContrato is null || evento.Valor is null
            || evento.DataPagamento is null || evento.StatusPagamento is null)
        {
            throw new DomainException("Evento sem dados validados nao pode ser aplicado ao contrato.");
        }

        var contrato = await contratos.ObterPorContratoAsync(evento.IdContrato, cancellationToken);
        if (contrato is null)
        {
            contrato = StatusContrato.Novo(evento.IdContrato);
            await contratos.AdicionarAsync(contrato, cancellationToken);
        }

        contrato.AplicarPagamento(
            evento.IdTransacao,
            evento.Valor.Value,
            evento.DataPagamento.Value,
            evento.StatusPagamento.Value);
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Evento processado. id_transacao={IdTransacao} duracao_ms={DuracaoMs}")]
    private static partial void LogProcessado(ILogger logger, string idTransacao, long duracaoMs);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning,
        Message = "Falha ao processar evento; nova tentativa agendada. "
                  + "id_transacao={IdTransacao} tentativa={Tentativas} proxima={Proxima:O}")]
    private static partial void LogRetentativaAgendada(
        ILogger logger, Exception excecao, string idTransacao, int tentativas, DateTime proxima);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Debug,
        Message = "Entrega ignorada: evento {EventoId} nao estava reivindicavel.")]
    private static partial void LogEntregaIgnorada(ILogger logger, Guid eventoId);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Error,
        Message = "Evento esgotou as tentativas e foi para a dead-letter queue. "
                  + "id_transacao={IdTransacao} tentativas={Tentativas}")]
    private static partial void LogDeadLetter(ILogger logger, Exception excecao, string idTransacao, int tentativas);
}
