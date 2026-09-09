namespace Sabemi.Pagamentos.Domain.Eventos;

/// <summary>
/// Estado do evento dentro do pipeline de recebimento.
/// </summary>
/// <remarks>
/// <para><see cref="Recebido"/> — gravado no log bruto e anunciado no outbox.</para>
/// <para><see cref="Processando"/> — reivindicado por um worker.</para>
/// <para><see cref="Processado"/> — regra de negocio aplicada com sucesso.</para>
/// <para><see cref="Invalido"/> — reprovado na validacao; nunca chegou a entrar na fila.</para>
/// <para><see cref="AguardandoRetentativa"/> — quebrou no processamento e tem nova tentativa agendada.</para>
/// <para><see cref="Falha"/> — esgotou as tentativas; parou na dead-letter queue.</para>
/// <para>
/// <see cref="Invalido"/> e <see cref="Falha"/> existem separados de proposito: o
/// primeiro e culpa do payload e nao adianta reprocessar; o segundo e falha de
/// execucao que ja foi retentada automaticamente e continuou quebrando.
/// </para>
/// </remarks>
public enum StatusProcessamento
{
    Recebido = 1,
    Processando = 2,
    Processado = 3,
    Invalido = 4,
    Falha = 5,
    AguardandoRetentativa = 6,
}

/// <summary>Agrupamento usado pelos filtros do painel administrativo.</summary>
public enum ResultadoEvento
{
    Pendente = 1,
    Sucesso = 2,
    Erro = 3,
}

public static class StatusProcessamentoExtensions
{
    /// <summary>
    /// Evento em retentativa conta como pendente, nao como erro: ele ainda tem
    /// chance de terminar bem, e classifica-lo como erro encheria o alerta do
    /// painel de falhas que o proprio sistema esta resolvendo sozinho.
    /// </summary>
    public static ResultadoEvento ParaResultado(this StatusProcessamento status) => status switch
    {
        StatusProcessamento.Processado => ResultadoEvento.Sucesso,
        StatusProcessamento.Invalido or StatusProcessamento.Falha => ResultadoEvento.Erro,
        _ => ResultadoEvento.Pendente,
    };

    public static IReadOnlyList<StatusProcessamento> Membros(this ResultadoEvento resultado) => resultado switch
    {
        ResultadoEvento.Sucesso => [StatusProcessamento.Processado],
        ResultadoEvento.Erro => [StatusProcessamento.Invalido, StatusProcessamento.Falha],
        _ =>
        [
            StatusProcessamento.Recebido,
            StatusProcessamento.Processando,
            StatusProcessamento.AguardandoRetentativa,
        ],
    };
}
