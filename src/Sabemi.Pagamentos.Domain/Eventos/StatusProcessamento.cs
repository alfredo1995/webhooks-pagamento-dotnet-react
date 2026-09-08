namespace Sabemi.Pagamentos.Domain.Eventos;

/// <summary>
/// Estado do evento dentro do pipeline de recebimento.
/// </summary>
/// <remarks>
/// <para><see cref="Recebido"/> — gravado no log bruto e enfileirado.</para>
/// <para><see cref="Processando"/> — retirado da fila pelo worker.</para>
/// <para><see cref="Processado"/> — regra de negocio aplicada com sucesso.</para>
/// <para><see cref="Invalido"/> — reprovado na validacao; nunca chegou a entrar na fila.</para>
/// <para><see cref="Falha"/> — entrou na fila e quebrou durante o processamento.</para>
/// <para>
/// <see cref="Invalido"/> e <see cref="Falha"/> existem separados de proposito: o
/// primeiro e culpa do payload e nao adianta reprocessar; o segundo e falha de
/// execucao e e candidato a retentativa.
/// </para>
/// </remarks>
public enum StatusProcessamento
{
    Recebido = 1,
    Processando = 2,
    Processado = 3,
    Invalido = 4,
    Falha = 5,
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
        _ => [StatusProcessamento.Recebido, StatusProcessamento.Processando],
    };
}
