using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Application.Processamento;

namespace Sabemi.Pagamentos.Infrastructure.Processamento;

/// <summary>
/// Fila em processo baseada em <see cref="Channel{T}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Adapter padrao: suficiente enquanto ha uma instancia da API, e sem
/// infraestrutura extra para rodar o projeto. Perder o processo nao perde
/// trabalho, porque a mensagem so existe depois de o evento estar commitado e o
/// supervisor devolve para a fila o que ficou sem desfecho.
/// </para>
/// <para>
/// A fila e limitada (<see cref="OpcoesProcessamento.CapacidadeFila"/>) e com
/// <see cref="BoundedChannelFullMode.Wait"/>: sob rajada, o publicador espera em
/// vez de estourar a memoria. E contrapressao honesta, e nao um buffer infinito
/// que adia o problema.
/// </para>
/// </remarks>
public sealed class FilaProcessamentoEmMemoria : IFilaProcessamento
{
    private readonly Channel<MensagemProcessamento> _canal;
    private int _aguardando;

    public FilaProcessamentoEmMemoria(IOptions<OpcoesProcessamento> opcoes)
    {
        ArgumentNullException.ThrowIfNull(opcoes);

        _canal = Channel.CreateBounded<MensagemProcessamento>(
            new BoundedChannelOptions(opcoes.Value.CapacidadeFila)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
            });
    }

    public int Aguardando => Volatile.Read(ref _aguardando);

    public async ValueTask PublicarAsync(
        MensagemProcessamento mensagem,
        CancellationToken cancellationToken = default)
    {
        await _canal.Writer.WriteAsync(mensagem, cancellationToken);

        Interlocked.Increment(ref _aguardando);
    }

    /// <summary>Consumido pelo worker de background.</summary>
    public IAsyncEnumerable<MensagemProcessamento> LerAsync(CancellationToken cancellationToken)
        => _canal.Reader.ReadAllAsync(cancellationToken);

    public void ConfirmarRetirada() => Interlocked.Decrement(ref _aguardando);

    public void Encerrar() => _canal.Writer.TryComplete();
}
