using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Sabemi.Pagamentos.Application.Processamento;

namespace Sabemi.Pagamentos.Infrastructure.Processamento.RabbitMq;

/// <summary>
/// Conexao unica com o broker, compartilhada por publicador e consumidor.
/// </summary>
/// <remarks>
/// <para>
/// Conexao AMQP e cara e pensada para viver o processo inteiro; o que se cria
/// por uso e o canal. Abrir uma conexao por publicacao esgotaria os file
/// descriptors do broker sob carga.
/// </para>
/// <para>
/// A API sobe antes do broker com frequencia — em <c>docker compose</c>, num
/// rolling deploy —, entao a conexao e preguicosa e refeita sob demanda. A
/// recuperacao automatica do proprio cliente cobre as quedas depois disso, e
/// enquanto o broker estiver fora as mensagens ficam no outbox, que e onde elas
/// estao seguras.
/// </para>
/// </remarks>
public sealed partial class ConexaoRabbitMq : IDisposable
{
    private readonly ConnectionFactory _fabrica;
    private readonly ILogger<ConexaoRabbitMq> _logger;
    private readonly SemaphoreSlim _porta = new(1, 1);

    private IConnection? _conexao;
    private bool _descartada;

    public ConexaoRabbitMq(IOptions<OpcoesProcessamento> opcoes, ILogger<ConexaoRabbitMq> logger)
    {
        ArgumentNullException.ThrowIfNull(opcoes);

        var configuracao = opcoes.Value.RabbitMq;
        _logger = logger;

        _fabrica = new ConnectionFactory
        {
            HostName = configuracao.Host,
            Port = configuracao.Porta,
            UserName = configuracao.Usuario,
            Password = configuracao.Senha,
            VirtualHost = configuracao.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
            DispatchConsumersAsync = true,
        };
    }

    public async Task<IConnection> ObterAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_descartada, this);

        if (_conexao is { IsOpen: true })
        {
            return _conexao;
        }

        await _porta.WaitAsync(cancellationToken);

        try
        {
            if (_conexao is { IsOpen: true })
            {
                return _conexao;
            }

            _conexao?.Dispose();
            _conexao = _fabrica.CreateConnection("sabemi-pagamentos");

            LogConectado(_logger, _fabrica.HostName, _fabrica.Port);

            return _conexao;
        }
        finally
        {
            _porta.Release();
        }
    }

    /// <summary>Declara a fila como durable: mensagem aceita precisa sobreviver a reinicio do broker.</summary>
    public static void DeclararFila(IModel canal, string nome)
    {
        ArgumentNullException.ThrowIfNull(canal);

        canal.QueueDeclare(nome, durable: true, exclusive: false, autoDelete: false, arguments: null);
    }

    public void Dispose()
    {
        if (_descartada)
        {
            return;
        }

        _descartada = true;
        _conexao?.Dispose();
        _porta.Dispose();
    }

    [LoggerMessage(EventId = 3301, Level = LogLevel.Information,
        Message = "Conectado ao RabbitMQ em {Host}:{Porta}.")]
    private static partial void LogConectado(ILogger logger, string host, int porta);
}
