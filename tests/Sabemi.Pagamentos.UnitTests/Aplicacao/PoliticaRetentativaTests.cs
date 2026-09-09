using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Application.Processamento;

namespace Sabemi.Pagamentos.UnitTests.Aplicacao;

public class PoliticaRetentativaTests
{
    private static readonly DateTime Agora = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid Evento = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static PoliticaRetentativaExponencial Criar(double jitter = 0, int maximo = 5)
        => new(Options.Create(new OpcoesProcessamento
        {
            Retentativa =
            {
                MaximoTentativas = maximo,
                BackoffBaseSegundos = 5,
                Fator = 2,
                BackoffMaximoSegundos = 60,
                JitterPercentual = jitter,
            },
        }));

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(3, 20)]
    [InlineData(4, 40)]
    public void CalcularProximaTentativa_CresceExponencialmente(int tentativasFeitas, int segundosEsperados)
    {
        var proxima = Criar().CalcularProximaTentativa(Evento, tentativasFeitas, Agora);

        proxima.Should().Be(Agora.AddSeconds(segundosEsperados));
    }

    [Fact]
    public void CalcularProximaTentativa_RespeitaOTeto()
    {
        // Sem teto, a decima tentativa cairia a mais de um dia de distancia:
        // insistir mais devagar nao aumenta a chance, so atrasa o diagnostico.
        var proxima = Criar().CalcularProximaTentativa(Evento, 10, Agora);

        proxima.Should().Be(Agora.AddSeconds(60));
    }

    [Fact]
    public void CalcularProximaTentativa_ComJitter_EspalhaDentroDaJanelaEContinuaReproduzivel()
    {
        var politica = Criar(jitter: 0.5);

        var primeira = politica.CalcularProximaTentativa(Evento, 1, Agora);
        var repetida = politica.CalcularProximaTentativa(Evento, 1, Agora);
        var outroEvento = politica.CalcularProximaTentativa(Guid.NewGuid(), 1, Agora);

        primeira.Should().BeOnOrAfter(Agora.AddSeconds(5)).And.BeOnOrBefore(Agora.AddSeconds(7.5));

        // Mesmo evento, mesmo agendamento: o backoff e diagnosticavel.
        repetida.Should().Be(primeira);

        // Eventos diferentes nao voltam todos no mesmo instante.
        outroEvento.Should().NotBe(primeira);
    }

    [Fact]
    public void PodeRetentar_AcabaNoMaximoConfigurado()
    {
        var politica = Criar(maximo: 3);

        politica.PodeRetentar(0).Should().BeTrue();
        politica.PodeRetentar(2).Should().BeTrue();
        politica.PodeRetentar(3).Should().BeFalse();
    }
}
