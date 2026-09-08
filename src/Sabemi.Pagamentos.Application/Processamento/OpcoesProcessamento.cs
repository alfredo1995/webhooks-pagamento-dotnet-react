namespace Sabemi.Pagamentos.Application.Processamento;

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

    /// <summary>Reenfileira, no startup, eventos que ficaram pendentes de uma execucao anterior.</summary>
    public bool RecuperarPendentesNoStartup { get; set; } = true;
}
