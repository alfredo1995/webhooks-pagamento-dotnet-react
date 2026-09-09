namespace Sabemi.Pagamentos.Application.Processamento;

/// <summary>Qual adapter de fila o processamento usa.</summary>
public enum ProvedorFila
{
    /// <summary>Channel em processo. Suficiente para uma instancia so.</summary>
    Memoria = 1,

    /// <summary>Broker externo, para quando ha mais de uma instancia da API.</summary>
    RabbitMq = 2,
}

public sealed class OpcoesProcessamento
{
    public const string Secao = "Processamento";

    /// <summary>
    /// Atraso que simula a regra de negocio pesada exigida pelo desafio.
    /// Configuravel para que os testes rodem sem esperar.
    /// </summary>
    public int AtrasoSimuladoMs { get; set; } = 2000;

    /// <summary>Quantos eventos o worker processa em paralelo.</summary>
    public int Concorrencia { get; set; } = 4;

    /// <summary>Capacidade da fila em memoria antes de aplicar contrapressao.</summary>
    public int CapacidadeFila { get; set; } = 1000;

    public ProvedorFila Fila { get; set; } = ProvedorFila.Memoria;

    /// <summary>Varre eventos pendentes assim que a aplicacao sobe.</summary>
    public bool RecuperarPendentesNoStartup { get; set; } = true;

    /// <summary>
    /// Prazo do lease de processamento. Passado esse tempo sem desfecho, o evento
    /// volta a ser reivindicavel — e o que impede que a queda de uma instancia
    /// deixe eventos presos em <c>Processando</c> para sempre.
    /// </summary>
    public int LeaseProcessamentoSegundos { get; set; } = 120;

    /// <summary>De quanto em quanto tempo o supervisor procura eventos para reentregar.</summary>
    public int IntervaloSupervisorSegundos { get; set; } = 10;

    /// <summary>Teto de eventos reentregues por ciclo do supervisor.</summary>
    public int LimiteReentregaPorCiclo { get; set; } = 200;

    public OpcoesRetentativa Retentativa { get; init; } = new();

    public OpcoesOutbox Outbox { get; init; } = new();

    public OpcoesRabbitMq RabbitMq { get; init; } = new();
}

/// <summary>Backoff exponencial com teto e jitter.</summary>
public sealed class OpcoesRetentativa
{
    /// <summary>Tentativas de processamento antes da dead-letter queue.</summary>
    public int MaximoTentativas { get; set; } = 5;

    public int BackoffBaseSegundos { get; set; } = 5;

    public double Fator { get; set; } = 2;

    /// <summary>Teto do intervalo: sem ele, a sexta tentativa cairia em horas.</summary>
    public int BackoffMaximoSegundos { get; set; } = 300;

    /// <summary>
    /// Dispersao aplicada ao intervalo, de 0 a 1. Existe para que uma queda de
    /// dependencia que derrubou mil eventos ao mesmo tempo nao os traga de volta
    /// todos no mesmo segundo.
    /// </summary>
    public double JitterPercentual { get; set; } = 0.2;
}

public sealed class OpcoesOutbox
{
    /// <summary>Intervalo entre varreduras do publicador.</summary>
    public int IntervaloMs { get; set; } = 1000;

    public int TamanhoLote { get; set; } = 50;

    /// <summary>Validade da reserva de um lote por um publicador.</summary>
    public int ReservaSegundos { get; set; } = 30;

    /// <summary>Espera antes de tentar publicar de novo uma mensagem recusada pelo broker.</summary>
    public int BackoffPublicacaoSegundos { get; set; } = 5;
}

public sealed class OpcoesRabbitMq
{
    public string Host { get; set; } = "localhost";

    public int Porta { get; set; } = 5672;

    public string Usuario { get; set; } = "guest";

    public string Senha { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    public string Fila { get; set; } = "sabemi.pagamentos.eventos";

    /// <summary>Mensagens entregues sem ack antes de o broker parar de empurrar.</summary>
    public ushort Prefetch { get; set; } = 8;
}
