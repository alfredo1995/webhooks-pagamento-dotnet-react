using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Domain.Outbox;

/// <summary>
/// Anuncio de que um evento foi aceito, gravado na mesma transacao do evento.
/// </summary>
/// <remarks>
/// <para>
/// E o padrao outbox: publicar direto no broker dentro do recebimento criaria
/// duas escritas sem transacao comum — se o commit no banco falhasse depois do
/// publish, o worker processaria um evento que nao existe; se o publish falhasse
/// depois do commit, o evento ficaria gravado e nunca processado. Gravando a
/// mensagem junto do evento, o banco decide os dois destinos de uma vez, e um
/// publicador em background leva o que ficou pendente para a fila.
/// </para>
/// <para>
/// A mensagem carrega o <c>traceparent</c> da requisicao HTTP que a originou.
/// E ele que costura o trace: sem esse campo, o processamento em background
/// apareceria no OpenTelemetry como um trace solto, sem ligacao com a chamada do
/// banco parceiro que o provocou.
/// </para>
/// <para>
/// A reserva (<see cref="ReservaToken"/> e <see cref="ReservadaAteUtc"/>) existe
/// para o caso de mais de uma instancia da API publicando ao mesmo tempo: cada
/// publicador reserva um lote com um token proprio e so enxerga o que reservou.
/// A reserva vence sozinha, entao a queda de uma instancia nao prende mensagem.
/// </para>
/// </remarks>
public class MensagemOutbox : Entity, IAggregateRoot
{
    public const string TipoEventoRecebido = "evento-webhook.recebido";

    /// <summary>Construtor exigido pelo EF Core.</summary>
    private MensagemOutbox() => Tipo = null!;

    private MensagemOutbox(string tipo, Guid eventoId, string? traceParent, string? traceState)
    {
        GerarIdentidade();

        Tipo = tipo;
        EventoId = eventoId;
        TraceParent = traceParent;
        TraceState = traceState;
        CriadaEmUtc = DateTime.UtcNow;
        ProximaTentativaEmUtc = CriadaEmUtc;
    }

    public string Tipo { get; private set; }

    public Guid EventoId { get; private set; }

    /// <summary>Contexto de trace W3C da requisicao que originou a mensagem.</summary>
    public string? TraceParent { get; private set; }

    public string? TraceState { get; private set; }

    public DateTime CriadaEmUtc { get; private set; }

    public DateTime? PublicadaEmUtc { get; private set; }

    public int Tentativas { get; private set; }

    public DateTime ProximaTentativaEmUtc { get; private set; }

    public string? UltimoErro { get; private set; }

    public string? ReservaToken { get; private set; }

    public DateTime? ReservadaAteUtc { get; private set; }

    public bool Publicada => PublicadaEmUtc is not null;

    public static MensagemOutbox ParaEventoRecebido(Guid eventoId, string? traceParent, string? traceState = null)
    {
        if (eventoId == Guid.Empty)
        {
            throw new DomainException("Mensagem de outbox precisa referenciar um evento.");
        }

        return new MensagemOutbox(TipoEventoRecebido, eventoId, traceParent, traceState);
    }

    public void MarcarPublicada()
    {
        PublicadaEmUtc = DateTime.UtcNow;
        UltimoErro = null;
        ReservaToken = null;
        ReservadaAteUtc = null;
    }

    /// <summary>O broker recusou a publicacao: solta a reserva e adia a proxima tentativa.</summary>
    public void RegistrarFalhaDePublicacao(string erro, DateTime proximaTentativaEmUtc)
    {
        Tentativas++;
        UltimoErro = string.IsNullOrWhiteSpace(erro) ? "Erro nao informado." : Truncar(erro);
        ProximaTentativaEmUtc = proximaTentativaEmUtc;
        ReservaToken = null;
        ReservadaAteUtc = null;
    }

    private static string Truncar(string texto) => texto.Length <= 1000 ? texto : texto[..1000];
}
