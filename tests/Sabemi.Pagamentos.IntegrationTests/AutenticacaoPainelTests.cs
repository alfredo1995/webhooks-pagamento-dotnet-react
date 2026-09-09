using System.Net;
using System.Net.Http.Json;
using Sabemi.Pagamentos.Api.Controllers;
using Sabemi.Pagamentos.Application.Consultas;
using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.IntegrationTests;

/// <summary>
/// O painel deixou de ser aberto: consultar exige token, e cada consulta deixa
/// rastro de quem a fez.
/// </summary>
public class AutenticacaoPainelTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/api/eventos")]
    [InlineData("/api/contratos")]
    [InlineData("/api/metricas")]
    [InlineData("/api/dead-letters")]
    [InlineData("/api/auditoria")]
    public async Task Endpoints_SemToken_Retornam401(string rota)
    {
        var resposta = await fabrica.CriarClienteAnonimo().GetAsync(new Uri(rota, UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_ComCredenciaisValidas_DevolveTokenUtilizavel()
    {
        var cliente = fabrica.CriarClienteAnonimo();

        var resposta = await cliente.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new LoginRequest(ApiFactory.Administrador, ApiFactory.SenhaPainel));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);

        var corpo = await resposta.Content.ReadFromJsonAsync<LoginResposta>();
        corpo!.Papel.Should().Be("administrador");
        corpo.Token.Should().NotBeNullOrWhiteSpace();

        cliente.DefaultRequestHeaders.Authorization = new("Bearer", corpo.Token);
        (await cliente.GetAsync(new Uri("/api/metricas", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_ComSenhaErrada_Retorna401()
    {
        var resposta = await fabrica.CriarClienteAnonimo().PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new LoginRequest(ApiFactory.Administrador, "senha-errada"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Auditoria_ComPapelOperador_Retorna403()
    {
        // Ler a trilha revela quais contratos foram investigados e por quem:
        // e informacao de administracao, nao de operacao.
        var resposta = await fabrica.CriarClienteDoPainel(ApiFactory.Operador)
            .GetAsync(new Uri("/api/auditoria", UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Auditoria_RegistraUsuarioRotaEFiltrosDaConsulta()
    {
        var operador = fabrica.CriarClienteDoPainel(ApiFactory.Operador);
        await operador.GetAsync(new Uri("/api/eventos?idContrato=CT-AUDITADO", UriKind.Relative));

        var trilha = await AguardarRegistroAsync("CT-AUDITADO");

        trilha.Usuario.Should().Be(ApiFactory.Operador);
        trilha.Papel.Should().Be("operador");
        trilha.Metodo.Should().Be("GET");
        trilha.Recurso.Should().Be("/api/eventos");
        trilha.StatusHttp.Should().Be(200);
    }

    [Fact]
    public async Task Auditoria_NaoRegistraOPollingDeMetricas()
    {
        await fabrica.CriarClienteDoPainel(ApiFactory.Administrador)
            .GetAsync(new Uri("/api/metricas", UriKind.Relative));

        await Task.Delay(200);

        var pagina = await fabrica.CriarClienteDoPainel(ApiFactory.Administrador)
            .GetFromJsonAsync<PagedResult<AuditoriaResumo>>("/api/auditoria?recurso=/api/metricas");

        pagina!.TotalItens.Should().Be(0);
    }

    private async Task<AuditoriaResumo> AguardarRegistroAsync(string filtro, int tentativas = 50)
    {
        var admin = fabrica.CriarClienteDoPainel(ApiFactory.Administrador);

        for (var i = 0; i < tentativas; i++)
        {
            var pagina = await admin.GetFromJsonAsync<PagedResult<AuditoriaResumo>>(
                "/api/auditoria?recurso=/api/eventos");

            var registro = pagina?.Itens.FirstOrDefault(r => r.Consulta is not null && r.Consulta.Contains(filtro, StringComparison.Ordinal));
            if (registro is not null)
            {
                return registro;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"A consulta com o filtro '{filtro}' nao apareceu na trilha de auditoria.");
    }
}
