namespace Sabemi.Pagamentos.Domain.Outbox;

public interface IOutboxRepository
{
    /// <summary>
    /// Anexa a mensagem a unidade de trabalho corrente. O commit acontece junto
    /// com o evento que a originou — e essa transacao comum que da sentido ao outbox.
    /// </summary>
    Task AdicionarAsync(MensagemOutbox mensagem, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserva um lote de mensagens ainda nao publicadas para este publicador.
    /// </summary>
    /// <remarks>
    /// A reserva e um UPDATE condicional que carimba o <paramref name="token"/>;
    /// a leitura seguinte devolve apenas o que este publicador conseguiu marcar.
    /// Dois publicadores concorrentes nunca recebem a mesma mensagem, e a reserva
    /// vence sozinha se quem reservou morrer antes de publicar.
    /// </remarks>
    Task<IReadOnlyList<MensagemOutbox>> ReservarLoteAsync(
        string token,
        DateTime agoraUtc,
        TimeSpan duracaoReserva,
        int limite,
        CancellationToken cancellationToken = default);

    Task<int> ContarPendentesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Descarta mensagens ja publicadas antes do corte.
    /// </summary>
    /// <remarks>
    /// Um outbox sem expurgo vira a maior tabela do banco e degrada justamente a
    /// consulta que roda a cada ciclo do publicador. O que ja foi publicado nao
    /// tem valor de auditoria: a verdade sobre o evento esta no log bruto.
    /// </remarks>
    Task<int> RemoverPublicadasAsync(DateTime anterioresAUtc, CancellationToken cancellationToken = default);
}
