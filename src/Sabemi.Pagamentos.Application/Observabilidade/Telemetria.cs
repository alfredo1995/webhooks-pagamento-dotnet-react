using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sabemi.Pagamentos.Application.Observabilidade;

/// <summary>
/// Fonte de traces e metricas da aplicacao.
/// </summary>
/// <remarks>
/// <para>
/// A instrumentacao mora na camada de aplicacao, e nao na borda HTTP, porque o
/// que interessa observar sao os passos do caso de uso — recebeu, publicou,
/// processou, retentou, morreu na DLQ. Instrumentar so o controller mostraria
/// apenas um <c>202</c> rapido e esconderia todo o trabalho que acontece depois
/// da resposta, que e exatamente onde as coisas quebram.
/// </para>
/// <para>
/// <see cref="ActivitySource"/> e <see cref="Meter"/> sao da BCL: a aplicacao nao
/// referencia OpenTelemetry. Quem escolhe o exportador e a composicao na API.
/// </para>
/// </remarks>
public static class Telemetria
{
    public const string Nome = "Sabemi.Pagamentos";

    public static readonly ActivitySource Fonte = new(Nome, "1.0.0");

    private static readonly Meter Medidor = new(Nome, "1.0.0");

    public static readonly Counter<long> EventosRecebidos = Medidor.CreateCounter<long>(
        "sabemi.eventos.recebidos",
        unit: "{evento}",
        description: "Webhooks aceitos, duplicados ou reprovados na validacao.");

    public static readonly Counter<long> EventosProcessados = Medidor.CreateCounter<long>(
        "sabemi.eventos.processados",
        unit: "{evento}",
        description: "Eventos que concluiram o processamento com sucesso.");

    public static readonly Counter<long> EventosRetentados = Medidor.CreateCounter<long>(
        "sabemi.eventos.retentados",
        unit: "{evento}",
        description: "Falhas de processamento que geraram nova tentativa agendada.");

    public static readonly Counter<long> EventosEmDeadLetter = Medidor.CreateCounter<long>(
        "sabemi.eventos.dead_letter",
        unit: "{evento}",
        description: "Eventos que esgotaram as tentativas e pararam na dead-letter queue.");

    public static readonly Counter<long> MensagensPublicadas = Medidor.CreateCounter<long>(
        "sabemi.outbox.publicadas",
        unit: "{mensagem}",
        description: "Mensagens do outbox entregues ao broker.");

    public static readonly Histogram<double> DuracaoProcessamento = Medidor.CreateHistogram<double>(
        "sabemi.eventos.duracao",
        unit: "ms",
        description: "Tempo de processamento da regra de negocio por evento.");

    /// <summary>
    /// Abre uma atividade filha do trace que originou a mensagem.
    /// </summary>
    /// <remarks>
    /// Sem o <c>traceparent</c> vindo do outbox, o processamento em background
    /// comecaria um trace novo e a jornada apareceria partida em duas: a
    /// requisicao do banco parceiro de um lado, o trabalho que ela provocou do
    /// outro, sem nada ligando as duas pontas.
    /// </remarks>
    public static Activity? IniciarConsumo(string nome, string? traceParent, string? traceState)
    {
        if (!string.IsNullOrWhiteSpace(traceParent)
            && ActivityContext.TryParse(traceParent, traceState, out var contexto))
        {
            return Fonte.StartActivity(nome, ActivityKind.Consumer, contexto);
        }

        return Fonte.StartActivity(nome, ActivityKind.Consumer);
    }

    /// <summary>Contexto W3C da atividade em andamento, para viajar com a mensagem.</summary>
    public static (string? TraceParent, string? TraceState) ContextoAtual()
    {
        var atual = Activity.Current;

        return atual is null || atual.IdFormat != ActivityIdFormat.W3C
            ? (null, null)
            : (atual.Id, atual.TraceStateString);
    }
}
