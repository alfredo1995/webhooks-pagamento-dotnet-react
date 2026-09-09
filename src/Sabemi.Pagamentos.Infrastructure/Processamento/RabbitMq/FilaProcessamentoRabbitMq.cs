using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Sabemi.Pagamentos.Application.Processamento;

namespace Sabemi.Pagamentos.Infrastructure.Processamento.RabbitMq;

/// <summary>
/// Adapter de fila para RabbitMQ, usado quando ha mais de uma instancia da API.
/// </summary>
/// <remarks>
/// <para>
/// A mensagem leva o identificador do evento e os cabecalhos <c>traceparent</c>
/// e <c>tracestate</c>. E assim que o trace atravessa o broker: sem esses
/// cabecalhos, o processamento apareceria no OpenTelemetry desligado da
/// requisicao que o originou.
/// </para>
/// <para>
/// Publicacao persistente em fila durable — o par minimo para que o broker nao
/// perca a mensagem ao reiniciar. Nao ha publisher confirms porque quem garante a
/// entrega e o outbox: a mensagem so sai da tabela depois que este metodo retorna
/// sem excecao.
/// </para>
/// </remarks>
public sealed class FilaProcessamentoRabbitMq(
    ConexaoRabbitMq conexao,
    IOptions<OpcoesProcessamento> opcoes) : IFilaProcessamento, IDisposable
{
    private static readonly TimeSpan ValidadeContagem = TimeSpan.FromSeconds(2);

    private readonly OpcoesRabbitMq _opcoes = opcoes.Value.RabbitMq;
    private readonly SemaphoreSlim _porta = new(1, 1);

    private IModel? _canal;
    private int _contagem;
    private long _contagemEmTicks;
    private bool _descartada;

    /// <summary>
    /// Tamanho da fila no broker, com cache curto.
    /// </summary>
    /// <remarks>
    /// A propriedade e lida pelo painel a cada poucos segundos e um canal AMQP
    /// nao e thread-safe. O cache combinado com uma tentativa de trava sem espera
    /// resolve os dois lados: nenhuma requisicao do painel fica bloqueada atras de
    /// uma publicacao, e o numero exibido nunca esta muito atrasado.
    /// </remarks>
    public int Aguardando
    {
        get
        {
            var idade = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Interlocked.Read(ref _contagemEmTicks));

            if (_descartada || idade < ValidadeContagem || !_porta.Wait(0))
            {
                return Volatile.Read(ref _contagem);
            }

            try
            {
                if (_canal is { IsOpen: true })
                {
                    Volatile.Write(ref _contagem, (int)Math.Min(int.MaxValue, _canal.MessageCount(_opcoes.Fila)));
                }
            }
            catch (Exception excecao) when (excecao is not OutOfMemoryException)
            {
                // Metrica de painel nunca derruba a consulta: mantem o ultimo valor.
            }
            finally
            {
                Interlocked.Exchange(ref _contagemEmTicks, DateTime.UtcNow.Ticks);
                _porta.Release();
            }

            return Volatile.Read(ref _contagem);
        }
    }

    public async ValueTask PublicarAsync(
        MensagemProcessamento mensagem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mensagem);
        ObjectDisposedException.ThrowIf(_descartada, this);

        await _porta.WaitAsync(cancellationToken);

        try
        {
            var canal = await GarantirCanalAsync(cancellationToken);

            var propriedades = canal.CreateBasicProperties();
            propriedades.Persistent = true;
            propriedades.ContentType = "text/plain";
            propriedades.Headers = new Dictionary<string, object>(StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(mensagem.TraceParent))
            {
                propriedades.Headers["traceparent"] = Encoding.UTF8.GetBytes(mensagem.TraceParent);
            }

            if (!string.IsNullOrWhiteSpace(mensagem.TraceState))
            {
                propriedades.Headers["tracestate"] = Encoding.UTF8.GetBytes(mensagem.TraceState);
            }

            canal.BasicPublish(
                exchange: string.Empty,
                routingKey: _opcoes.Fila,
                basicProperties: propriedades,
                body: Encoding.UTF8.GetBytes(mensagem.EventoId.ToString("D", CultureInfo.InvariantCulture)));
        }
        finally
        {
            _porta.Release();
        }
    }

    public void Dispose()
    {
        if (_descartada)
        {
            return;
        }

        _descartada = true;
        _canal?.Dispose();
        _porta.Dispose();
    }

    private async Task<IModel> GarantirCanalAsync(CancellationToken cancellationToken)
    {
        if (_canal is { IsOpen: true })
        {
            return _canal;
        }

        _canal?.Dispose();

        var conexaoAberta = await conexao.ObterAsync(cancellationToken);
        _canal = conexaoAberta.CreateModel();

        ConexaoRabbitMq.DeclararFila(_canal, _opcoes.Fila);

        return _canal;
    }
}
