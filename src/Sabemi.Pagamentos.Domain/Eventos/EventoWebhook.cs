using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Domain.Eventos;

/// <summary>
/// Log de eventos brutos: o registro fiel do que o banco parceiro enviou.
/// </summary>
/// <remarks>
/// O payload original e guardado inteiro e nunca reescrito. Se amanha a
/// interpretacao dos campos mudar, ou se um evento precisar ser auditado, a
/// verdade continua aqui — os campos extraidos sao apenas uma projecao para
/// consulta. <see cref="IdTransacao"/> tem indice unico: e ele que garante a
/// idempotencia exigida quando o banco reenvia a mesma notificacao.
/// </remarks>
public class EventoWebhook : Entity, IAggregateRoot
{
    public const int TamanhoMaximoIdTransacao = 100;
    public const int TamanhoMaximoIdContrato = 100;

    /// <summary>Construtor exigido pelo EF Core.</summary>
    private EventoWebhook()
    {
        IdTransacao = null!;
        PayloadBruto = null!;
        OrigemParceiro = null!;
    }

    private EventoWebhook(string idTransacao, string payloadBruto, string origemParceiro)
    {
        GerarIdentidade();

        IdTransacao = idTransacao;
        PayloadBruto = payloadBruto;
        OrigemParceiro = origemParceiro;
        RecebidoEmUtc = DateTime.UtcNow;
        Status = StatusProcessamento.Recebido;
    }

    /// <summary>Chave de idempotencia enviada pelo banco.</summary>
    public string IdTransacao { get; private set; }

    public string PayloadBruto { get; private set; }

    public string OrigemParceiro { get; private set; }

    public DateTime RecebidoEmUtc { get; private set; }

    public StatusProcessamento Status { get; private set; }

    // Projecao dos campos do payload, preenchida somente quando o evento e valido.
    public string? IdContrato { get; private set; }

    public decimal? Valor { get; private set; }

    public DateTime? DataPagamento { get; private set; }

    public StatusPagamento? StatusPagamento { get; private set; }

    public string? MotivoFalha { get; private set; }

    public int Tentativas { get; private set; }

    public DateTime? ProcessadoEmUtc { get; private set; }

    public long? DuracaoProcessamentoMs { get; private set; }

    public bool Concluido => Status is StatusProcessamento.Processado
        or StatusProcessamento.Invalido or StatusProcessamento.Falha;

    /// <summary>
    /// Registra o recebimento antes de qualquer validacao. Um payload que chegou
    /// e um fato, valido ou nao — descartar o invalido esconderia justamente o
    /// caso que o painel precisa mostrar.
    /// </summary>
    public static EventoWebhook Registrar(string? idTransacao, string? payloadBruto, string? origemParceiro)
    {
        if (string.IsNullOrWhiteSpace(idTransacao))
        {
            throw new DomainException("id_transacao e obrigatorio para registrar o evento.");
        }

        var identificador = idTransacao.Trim();
        if (identificador.Length > TamanhoMaximoIdTransacao)
        {
            throw new DomainException($"id_transacao deve ter no maximo {TamanhoMaximoIdTransacao} caracteres.");
        }

        return new EventoWebhook(
            identificador,
            payloadBruto ?? string.Empty,
            string.IsNullOrWhiteSpace(origemParceiro) ? "desconhecido" : origemParceiro.Trim());
    }

    /// <summary>Preenche a projecao de consulta a partir de um payload ja validado.</summary>
    public void AplicarDadosValidados(string idContrato, decimal valor, DateTime dataPagamento, StatusPagamento status)
    {
        if (string.IsNullOrWhiteSpace(idContrato))
        {
            throw new DomainException("id_contrato e obrigatorio.");
        }

        IdContrato = idContrato.Trim();
        Valor = decimal.Round(valor, 2, MidpointRounding.AwayFromZero);
        DataPagamento = dataPagamento;
        StatusPagamento = status;
    }

    public void MarcarInvalido(string motivo)
    {
        GarantirNaoConcluido("marcar como invalido");

        Status = StatusProcessamento.Invalido;
        MotivoFalha = Truncar(motivo);
        ProcessadoEmUtc = DateTime.UtcNow;
    }

    public void IniciarProcessamento()
    {
        if (Status is not (StatusProcessamento.Recebido or StatusProcessamento.Falha))
        {
            throw new DomainException($"Evento com status {Status} nao pode iniciar processamento.");
        }

        Status = StatusProcessamento.Processando;
        Tentativas++;
    }

    public void ConcluirComSucesso(long duracaoMs)
    {
        if (Status != StatusProcessamento.Processando)
        {
            throw new DomainException($"Somente evento em processamento pode ser concluido. Status atual: {Status}.");
        }

        Status = StatusProcessamento.Processado;
        ProcessadoEmUtc = DateTime.UtcNow;
        DuracaoProcessamentoMs = duracaoMs;
        MotivoFalha = null;
    }

    public void RegistrarFalha(string motivo, long duracaoMs)
    {
        Status = StatusProcessamento.Falha;
        MotivoFalha = Truncar(motivo);
        ProcessadoEmUtc = DateTime.UtcNow;
        DuracaoProcessamentoMs = duracaoMs;
    }

    private void GarantirNaoConcluido(string acao)
    {
        if (Concluido)
        {
            throw new DomainException($"Nao e possivel {acao}: evento ja concluido com status {Status}.");
        }
    }

    private static string Truncar(string motivo)
    {
        var texto = string.IsNullOrWhiteSpace(motivo) ? "Motivo nao informado." : motivo.Trim();

        return texto.Length <= 1000 ? texto : texto[..1000];
    }
}
