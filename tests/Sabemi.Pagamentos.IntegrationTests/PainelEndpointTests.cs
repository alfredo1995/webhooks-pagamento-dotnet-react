using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Sabemi.Pagamentos.Application.Consultas;
using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.IntegrationTests;

/// <summary>Endpoints de leitura que alimentam o painel administrativo.</summary>
public class PainelEndpointTests(ApiFactory fabrica) : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _cliente = fabrica.CreateClient();

    public async Task InitializeAsync()
    {
        await EnviarAsync("TX-A1", "CT-ALFA", 100m, "CONFIRMADO");
        await EnviarAsync("TX-A2", "CT-ALFA", 50m, "CONFIRMADO");
        await EnviarAsync("TX-B1", "CT-BETA", 300m, "CONFIRMADO");
        await EnviarAsync("TX-RUIM", "", -1m, "NAO-EXISTE");

        await AguardarSemPendentesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metricas_RefletemOsEventosRecebidos()
    {
        var metricas = await _cliente.GetFromJsonAsync<Metricas>("/api/metricas");

        metricas!.Total.Should().Be(4);
        metricas.Sucesso.Should().Be(3);
        metricas.Erro.Should().Be(1);
        metricas.Pendentes.Should().Be(0);
        metricas.PorStatus.Should().ContainKey("Processado");
    }

    [Fact]
    public async Task Eventos_SemFiltro_TrazTodosDoMaisRecenteParaOMaisAntigo()
    {
        var pagina = await _cliente.GetFromJsonAsync<PagedResult<EventoResumo>>("/api/eventos");

        pagina!.TotalItens.Should().Be(4);
        pagina.Itens.Should().BeInDescendingOrder(e => e.RecebidoEmUtc);
    }

    [Fact]
    public async Task Eventos_FiltradosPorErro_TrazemApenasOsQueFalharam()
    {
        var pagina = await _cliente.GetFromJsonAsync<PagedResult<EventoResumo>>("/api/eventos?resultado=Erro");

        pagina!.Itens.Should().OnlyContain(e => e.Resultado == "Erro");
        pagina.Itens.Should().ContainSingle(e => e.IdTransacao == "TX-RUIM");
    }

    [Fact]
    public async Task Eventos_FiltradosPorSucesso_NaoTrazemErros()
    {
        var pagina = await _cliente.GetFromJsonAsync<PagedResult<EventoResumo>>("/api/eventos?resultado=Sucesso");

        pagina!.TotalItens.Should().Be(3);
        pagina.Itens.Should().OnlyContain(e => e.Resultado == "Sucesso");
    }

    [Fact]
    public async Task Eventos_FiltradosPorContrato_TrazemApenasOContrato()
    {
        var pagina = await _cliente.GetFromJsonAsync<PagedResult<EventoResumo>>("/api/eventos?idContrato=CT-ALFA");

        pagina!.TotalItens.Should().Be(2);
        pagina.Itens.Should().OnlyContain(e => e.IdContrato == "CT-ALFA");
    }

    [Fact]
    public async Task Eventos_ComFiltroDeResultadoInvalido_Retorna409ComMensagemUtil()
    {
        var resposta = await _cliente.GetAsync(new Uri("/api/eventos?resultado=banana", UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await resposta.Content.ReadAsStringAsync()).Should().Contain("Sucesso, Erro, Pendente ou Todos");
    }

    [Fact]
    public async Task Eventos_RespeitamOTetoDePaginacao()
    {
        var pagina = await _cliente.GetFromJsonAsync<PagedResult<EventoResumo>>("/api/eventos?tamanhoPagina=9999");

        pagina!.TamanhoPagina.Should().Be(200);
    }

    [Fact]
    public async Task Evento_PorId_TrazOPayloadBruto()
    {
        var pagina = await _cliente.GetFromJsonAsync<PagedResult<EventoResumo>>("/api/eventos?idTransacao=TX-B1");
        var id = pagina!.Itens[0].Id;

        var detalhe = await _cliente.GetFromJsonAsync<EventoDetalhe>($"/api/eventos/{id}");

        detalhe!.PayloadBruto.Should().Contain("TX-B1");
        detalhe.Resumo.IdContrato.Should().Be("CT-BETA");
    }

    [Fact]
    public async Task Evento_Inexistente_Retorna404()
    {
        var resposta = await _cliente.GetAsync(new Uri($"/api/eventos/{Guid.NewGuid()}", UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Contratos_ConsolidamOsPagamentosPorContrato()
    {
        var pagina = await _cliente.GetFromJsonAsync<PagedResult<ContratoResumo>>("/api/contratos");

        var alfa = pagina!.Itens.Single(c => c.IdContrato == "CT-ALFA");
        alfa.ValorTotalPago.Should().Be(150m);
        alfa.QuantidadePagamentos.Should().Be(2);
        alfa.SaldoLiquido.Should().Be(150m);

        pagina.Itens.Single(c => c.IdContrato == "CT-BETA").ValorTotalPago.Should().Be(300m);
    }

    [Fact]
    public async Task Health_RespondeHealthy()
    {
        var resposta = await _cliente.GetAsync(new Uri("/health", UriKind.Relative));

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task EnviarAsync(string idTransacao, string idContrato, decimal valor, string status)
    {
        var corpo = $$"""
        {
          "id_transacao": "{{idTransacao}}",
          "id_contrato": "{{idContrato}}",
          "valor": {{valor.ToString(CultureInfo.InvariantCulture)}},
          "data_pagamento": "2026-03-01T12:00:00Z",
          "status": "{{status}}"
        }
        """;

        await _cliente.SendAsync(ApiFactory.MontarRequisicao(corpo));
    }

    private async Task AguardarSemPendentesAsync(int tentativas = 100)
    {
        for (var i = 0; i < tentativas; i++)
        {
            var metricas = await _cliente.GetFromJsonAsync<Metricas>("/api/metricas");
            if (metricas is { Pendentes: 0, NaFila: 0, Total: > 0 })
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Os eventos de apoio nao concluiram o processamento a tempo.");
    }
}
