using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sabemi.Pagamentos.Application.Processamento;

namespace Sabemi.Pagamentos.Infrastructure.Processamento.RabbitMq;

/// <summary>
/// Consome a fila do broker e entrega cada mensagem ao processador.
/// </summary>
/// <remarks>
/// <para>
/// O <c>prefetch</c> limita quantas mensagens o broker empurra sem ack. Sem ele,
/// uma instancia puxaria a fila inteira para a memoria e as outras ficariam
/// ociosas — o oposto do motivo de existir um broker aqui.
/// </para>
/// <para>
/// O ack acontece sempre, inclusive quando o processamento quebra. Parece
/// contraintuitivo, mas o estado do evento vive no banco, e nao na mensagem: uma
/// falha de negocio ja virou retentativa agendada, e uma falha de infraestrutura
/// deixa o evento com o lease vencido, que o supervisor devolve para a fila. Usar
/// nack com requeue traria a mesma mensagem de volta em milissegundos, num laco
/// quente que ignoraria o backoff que existe justamente para evitar isso.
/// </para>
/// </remarks>
public sealed partial class ConsumidorRabbitMq(
    ConexaoRabbitMq conexao,
    IServiceScopeFactory escopos,
    IOptions<OpcoesProcessamento> opcoes,
    ILogger<ConsumidorRabbitMq> logger) : BackgroundService
{
    private static readonly TimeSpan EsperaAposFalha = TimeSpan.FromSeconds(5);

    private readonly OpcoesRabbitMq _opcoes = opcoes.Value.RabbitMq;

    private IModel? _canal;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AssinarAsync(stoppingToken);

                LogAssinado(logger, _opcoes.Fila, _opcoes.Prefetch);

                // A entrega vem por callback; este laco so mantem o servico vivo.
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception excecao)
            {
                // Broker ainda subindo ou conexao derrubada: tenta de novo. As
                // mensagens seguem no outbox enquanto isso.
                LogAssinaturaFalhou(logger, excecao);

                await Task.Delay(EsperaAposFalha, stoppingToken);
            }
        }
    }

    public override void Dispose()
    {
        _canal?.Dispose();

        base.Dispose();
    }

    private async Task AssinarAsync(CancellationToken stoppingToken)
    {
        _canal?.Dispose();

        var conexaoAberta = await conexao.ObterAsync(stoppingToken);

        _canal = conexaoAberta.CreateModel();
        ConexaoRabbitMq.DeclararFila(_canal, _opcoes.Fila);
        _canal.BasicQos(prefetchSize: 0, prefetchCount: _opcoes.Prefetch, global: false);

        var consumidor = new AsyncEventingBasicConsumer(_canal);
        consumidor.Received += (_, entrega) => TratarAsync(entrega, stoppingToken);

        _canal.BasicConsume(_opcoes.Fila, autoAck: false, consumer: consumidor);
    }

    private async Task TratarAsync(BasicDeliverEventArgs entrega, CancellationToken stoppingToken)
    {
        var canal = _canal;
        if (canal is null)
        {
            return;
        }

        var corpo = Encoding.UTF8.GetString(entrega.Body.Span);

        if (!Guid.TryParse(corpo, out var eventoId))
        {
            LogMensagemIlegivel(logger, corpo);
            canal.BasicAck(entrega.DeliveryTag, multiple: false);

            return;
        }

        try
        {
            using var escopo = escopos.CreateScope();
            var processador = escopo.ServiceProvider.GetRequiredService<IProcessadorPagamento>();

            await processador.ProcessarAsync(
                new MensagemProcessamento(eventoId, LerCabecalho(entrega, "traceparent"), LerCabecalho(entrega, "tracestate")),
                stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Desligamento: sem ack, o broker reentrega para outra instancia.
            return;
        }
        catch (Exception excecao)
        {
            LogEntregaFalhou(logger, excecao, eventoId);
        }

        canal.BasicAck(entrega.DeliveryTag, multiple: false);
    }

    private static string? LerCabecalho(BasicDeliverEventArgs entrega, string nome)
    {
        if (entrega.BasicProperties?.Headers is null
            || !entrega.BasicProperties.Headers.TryGetValue(nome, out var valor))
        {
            return null;
        }

        return valor switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string texto => texto,
            _ => null,
        };
    }

    [LoggerMessage(EventId = 3401, Level = LogLevel.Information,
        Message = "Consumidor RabbitMQ assinado na fila {Fila}. prefetch={Prefetch}")]
    private static partial void LogAssinado(ILogger logger, string fila, ushort prefetch);

    [LoggerMessage(EventId = 3402, Level = LogLevel.Warning,
        Message = "Nao foi possivel assinar a fila do RabbitMQ; nova tentativa em instantes.")]
    private static partial void LogAssinaturaFalhou(ILogger logger, Exception excecao);

    [LoggerMessage(EventId = 3403, Level = LogLevel.Error,
        Message = "Erro inesperado ao consumir o evento {EventoId}.")]
    private static partial void LogEntregaFalhou(ILogger logger, Exception excecao, Guid eventoId);

    [LoggerMessage(EventId = 3404, Level = LogLevel.Warning,
        Message = "Mensagem descartada: corpo nao e um identificador de evento. corpo={Corpo}")]
    private static partial void LogMensagemIlegivel(ILogger logger, string corpo);
}
