using Microsoft.Extensions.Options;

namespace Sabemi.Pagamentos.Application.Processamento;

public interface IPoliticaRetentativa
{
    int MaximoTentativas { get; }

    /// <summary>Ainda ha orcamento depois de <paramref name="tentativasFeitas"/> tentativas?</summary>
    bool PodeRetentar(int tentativasFeitas);

    DateTime CalcularProximaTentativa(Guid eventoId, int tentativasFeitas, DateTime agoraUtc);
}

/// <summary>
/// Backoff exponencial com teto e jitter deterministico.
/// </summary>
/// <remarks>
/// <para>
/// Com base 5s e fator 2, os intervalos sao 5s, 10s, 20s, 40s e 80s — tempo
/// suficiente para uma dependencia reiniciar, sem transformar um erro permanente
/// em espera de horas. O teto existe porque, passado certo ponto, insistir mais
/// devagar nao aumenta a chance de sucesso: so atrasa o diagnostico.
/// </para>
/// <para>
/// O jitter e derivado do proprio identificador do evento, e nao de um gerador
/// aleatorio. Mil eventos que falharam juntos ainda se espalham na janela — que
/// e o objetivo —, e o intervalo continua reproduzivel: o mesmo evento sempre
/// recebe o mesmo agendamento, o que torna o comportamento testavel e o
/// diagnostico possivel.
/// </para>
/// </remarks>
public sealed class PoliticaRetentativaExponencial(IOptions<OpcoesProcessamento> opcoes) : IPoliticaRetentativa
{
    private readonly OpcoesRetentativa _opcoes = opcoes.Value.Retentativa;

    public int MaximoTentativas => Math.Max(1, _opcoes.MaximoTentativas);

    public bool PodeRetentar(int tentativasFeitas) => tentativasFeitas < MaximoTentativas;

    public DateTime CalcularProximaTentativa(Guid eventoId, int tentativasFeitas, DateTime agoraUtc)
    {
        var expoente = Math.Max(0, tentativasFeitas - 1);
        var baseSegundos = Math.Max(1, _opcoes.BackoffBaseSegundos);
        var fator = _opcoes.Fator <= 1 ? 1 : _opcoes.Fator;

        var segundos = baseSegundos * Math.Pow(fator, expoente);
        segundos = Math.Min(segundos, Math.Max(baseSegundos, _opcoes.BackoffMaximoSegundos));

        return agoraUtc.AddSeconds(segundos + Dispersao(eventoId, tentativasFeitas, segundos));
    }

    /// <summary>Deslocamento em segundos, entre zero e <c>jitter x intervalo</c>.</summary>
    private double Dispersao(Guid eventoId, int tentativasFeitas, double segundos)
    {
        var percentual = Math.Clamp(_opcoes.JitterPercentual, 0, 1);
        if (percentual == 0)
        {
            return 0;
        }

        // Guid.GetHashCode e estavel dentro e entre processos, ao contrario do
        // hash de string; combinado com a tentativa, cada rodada recebe um
        // deslocamento diferente sem depender de estado global.
        var semente = (uint)HashCode.Combine(eventoId, tentativasFeitas);
        var fracao = semente / (double)uint.MaxValue;

        return segundos * percentual * fracao;
    }
}
