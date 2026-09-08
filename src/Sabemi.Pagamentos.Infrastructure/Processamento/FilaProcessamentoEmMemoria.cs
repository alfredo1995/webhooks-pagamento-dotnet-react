using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Application.Processamento;

namespace Sabemi.Pagamentos.Infrastructure.Processamento;

/// <summary>
/// Fila em processo baseada em <see cref="Channel{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// A fila carrega apenas o identificador do evento, nunca o payload: o dado ja
/// esta gravado no log bruto antes de enfileirar. Isso mantem a fila leve e,
/// principalmente, faz do banco a fonte da verdade — se o processo cair com itens
/// na fila, nada se perde, porque os eventos continuam como pendentes no banco e
/// sao reenfileirados no proximo startup.
/// </para>
/// <para>
/// A fila e limitada (<see cref="OpcoesProcessamento.CapacidadeFila"/>) e com
/// <see cref="BoundedChannelFullMode.Wait"/>: sob rajada, o recebimento espera em
/// vez de estourar a memoria. E contrapressao honesta, e nao um buffer infinito
/// que adia o problema.
/// </para>
/// </remarks>
public sealed class FilaProcessamentoEmMemoria : IFilaProcessamento
{
    private readonly Channel<Guid> _canal;
    private int _aguardando;

    public FilaProcessamentoEmMemoria(IOptions<OpcoesProcessamento> opcoes)
    {
        ArgumentNullException.ThrowIfNull(opcoes);

        _canal = Channel.CreateBounded<Guid>(new BoundedChannelOptions(opcoes.Value.CapacidadeFila)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public int Aguardando => Volatile.Read(ref _aguardando);

    public async ValueTask EnfileirarAsync(Guid eventoId, CancellationToken cancellationToken = default)
    {
        await _canal.Writer.WriteAsync(eventoId, cancellationToken);

        Interlocked.Increment(ref _aguardando);
    }

    /// <summary>Consumido pelo worker de background.</summary>
    public IAsyncEnumerable<Guid> LerAsync(CancellationToken cancellationToken)
        => _canal.Reader.ReadAllAsync(cancellationToken);

    public void ConfirmarRetirada() => Interlocked.Decrement(ref _aguardando);

    public void Encerrar() => _canal.Writer.TryComplete();
}
