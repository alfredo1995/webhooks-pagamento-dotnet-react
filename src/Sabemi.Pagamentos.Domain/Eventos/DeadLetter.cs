using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Domain.Eventos;

/// <summary>
/// Carta morta: um evento que esgotou as retentativas automaticas e parou de ser
/// reprocessado sozinho.
/// </summary>
/// <remarks>
/// <para>
/// A dead-letter queue e uma tabela, e nao uma fila do broker, por dois motivos.
/// Primeiro, ela precisa sobreviver a troca de broker — hoje RabbitMQ, amanha
/// Service Bus — e a verdade sobre "o que falhou de vez" nao pode morar em
/// infraestrutura substituivel. Segundo, o valor de uma DLQ esta em ser
/// consultavel: o operador filtra, le o motivo e reprocessa pelo painel, coisa
/// que uma fila de mensagens nao oferece.
/// </para>
/// <para>
/// A carta guarda o motivo e o numero de tentativas da rodada que morreu. Quando
/// o evento e reprocessado a mao, a carta nao e apagada: ela e marcada como
/// reprocessada, com quem mandou. E o registro de que aquela falha existiu.
/// </para>
/// </remarks>
public class DeadLetter : Entity, IAggregateRoot
{
    /// <summary>Construtor exigido pelo EF Core.</summary>
    private DeadLetter()
    {
        IdTransacao = null!;
        Motivo = null!;
    }

    private DeadLetter(Guid eventoId, string idTransacao, string? idContrato, string motivo, int tentativas)
    {
        GerarIdentidade();

        EventoId = eventoId;
        IdTransacao = idTransacao;
        IdContrato = idContrato;
        Motivo = motivo;
        Tentativas = tentativas;
        CriadoEmUtc = DateTime.UtcNow;
    }

    public Guid EventoId { get; private set; }

    public string IdTransacao { get; private set; }

    public string? IdContrato { get; private set; }

    /// <summary>Erro da ultima tentativa, que e o que o operador le primeiro.</summary>
    public string Motivo { get; private set; }

    public int Tentativas { get; private set; }

    public DateTime CriadoEmUtc { get; private set; }

    public DateTime? ReprocessadoEmUtc { get; private set; }

    public string? ReprocessadoPor { get; private set; }

    public bool Pendente => ReprocessadoEmUtc is null;

    public static DeadLetter DoEvento(EventoWebhook evento)
    {
        ArgumentNullException.ThrowIfNull(evento);

        if (evento.Status != StatusProcessamento.Falha)
        {
            throw new DomainException(
                $"So vai para a dead-letter queue evento em Falha. Status atual: {evento.Status}.");
        }

        return new DeadLetter(
            evento.Id,
            evento.IdTransacao,
            evento.IdContrato,
            evento.MotivoFalha ?? "Motivo nao informado.",
            evento.Tentativas);
    }

    public void MarcarReprocessada(string usuario)
    {
        if (!Pendente)
        {
            throw new DomainException($"A carta da transacao {IdTransacao} ja foi reprocessada.");
        }

        ReprocessadoEmUtc = DateTime.UtcNow;
        ReprocessadoPor = string.IsNullOrWhiteSpace(usuario) ? "desconhecido" : usuario.Trim();
    }
}
