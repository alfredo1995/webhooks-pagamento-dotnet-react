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

    /// <summary>Quando a proxima tentativa automatica fica elegivel. Nulo fora de retentativa.</summary>
    public DateTime? ProximaTentativaEmUtc { get; private set; }

    /// <summary>
    /// Instante da ultima reivindicacao por um worker. E o relogio do lease: um
    /// evento que ficou <see cref="StatusProcessamento.Processando"/> alem do
    /// prazo volta a ser reivindicavel, senao a queda de uma instancia deixaria
    /// o evento preso para sempre.
    /// </summary>
    public DateTime? ReivindicadoEmUtc { get; private set; }

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

    /// <summary>
    /// Um worker pode assumir este evento agora?
    /// </summary>
    /// <remarks>
    /// Sao tres portas de entrada: o evento recem-recebido, a retentativa cujo
    /// horario chegou e o evento travado em processamento alem do lease — este
    /// ultimo e o que impede que a queda de uma instancia prenda o evento para
    /// sempre.
    /// </remarks>
    public bool PodeSerReivindicado(DateTime agoraUtc, TimeSpan lease) => Status switch
    {
        StatusProcessamento.Recebido => true,
        StatusProcessamento.AguardandoRetentativa =>
            ProximaTentativaEmUtc is null || ProximaTentativaEmUtc <= agoraUtc,
        StatusProcessamento.Processando =>
            ReivindicadoEmUtc is null || ReivindicadoEmUtc <= agoraUtc - lease,
        _ => false,
    };

    /// <summary>
    /// Assume o evento para processamento: conta a tentativa e carimba o lease.
    /// </summary>
    /// <remarks>
    /// O repositorio espelha estas atribuicoes em um UPDATE condicional, porque
    /// com mais de uma instancia a transicao precisa ser atomica no banco — ler,
    /// decidir em memoria e gravar deixaria uma janela em que dois workers
    /// reivindicam o mesmo evento. Este metodo continua sendo a definicao
    /// canonica da regra: e ele que diz quais estados podem ser reivindicados e
    /// o que muda quando isso acontece.
    /// </remarks>
    public void Reivindicar(DateTime agoraUtc, TimeSpan lease)
    {
        if (!PodeSerReivindicado(agoraUtc, lease))
        {
            throw new DomainException($"Evento com status {Status} nao pode ser reivindicado agora.");
        }

        Status = StatusProcessamento.Processando;
        Tentativas++;
        ReivindicadoEmUtc = agoraUtc;
    }

    public void ConcluirComSucesso(long duracaoMs)
    {
        GarantirEmProcessamento("concluir");

        Status = StatusProcessamento.Processado;
        ProcessadoEmUtc = DateTime.UtcNow;
        DuracaoProcessamentoMs = duracaoMs;
        MotivoFalha = null;
        ProximaTentativaEmUtc = null;
        ReivindicadoEmUtc = null;
    }

    /// <summary>
    /// A tentativa falhou, mas o orcamento de retentativas ainda nao acabou: o
    /// evento volta a ficar elegivel depois do backoff.
    /// </summary>
    public void AgendarRetentativa(string motivo, long duracaoMs, DateTime proximaTentativaEmUtc)
    {
        GarantirEmProcessamento("agendar retentativa");

        Status = StatusProcessamento.AguardandoRetentativa;
        MotivoFalha = Truncar(motivo);
        DuracaoProcessamentoMs = duracaoMs;
        ProximaTentativaEmUtc = proximaTentativaEmUtc;
        ReivindicadoEmUtc = null;
    }

    /// <summary>Fim da linha: as tentativas acabaram e o evento vai para a dead-letter queue.</summary>
    public void RegistrarFalha(string motivo, long duracaoMs)
    {
        Status = StatusProcessamento.Falha;
        MotivoFalha = Truncar(motivo);
        ProcessadoEmUtc = DateTime.UtcNow;
        DuracaoProcessamentoMs = duracaoMs;
        ProximaTentativaEmUtc = null;
        ReivindicadoEmUtc = null;
    }

    /// <summary>
    /// Devolve para a fila um evento que parou na dead-letter queue.
    /// </summary>
    /// <remarks>
    /// O contador de tentativas volta a zero de proposito: quem reprocessa a mao
    /// ja corrigiu a causa e espera um orcamento novo de retentativas. O historico
    /// da rodada anterior nao se perde — ele fica na carta da DLQ.
    /// </remarks>
    public void ReabrirParaReprocessamento()
    {
        if (Status is not StatusProcessamento.Falha)
        {
            throw new DomainException(
                $"Somente evento em Falha pode ser reprocessado manualmente. Status atual: {Status}.");
        }

        Status = StatusProcessamento.Recebido;
        Tentativas = 0;
        MotivoFalha = null;
        ProcessadoEmUtc = null;
        DuracaoProcessamentoMs = null;
        ProximaTentativaEmUtc = null;
        ReivindicadoEmUtc = null;
    }

    private void GarantirEmProcessamento(string acao)
    {
        if (Status != StatusProcessamento.Processando)
        {
            throw new DomainException($"Somente evento em processamento pode {acao}. Status atual: {Status}.");
        }
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
