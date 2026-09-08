using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sabemi.Pagamentos.Application.Consultas;
using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.IntegrationTests;

public class WebhookEndpointTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _cliente = fabrica.CreateClient();

    private static string Corpo(
        string idTransacao,
        string idContrato = "CT-1000",
        decimal valor = 250.00m,
        string status = "CONFIRMADO")
        => $$"""
        {
          "id_transacao": "{{idTransacao}}",
          "id_contrato": "{{idContrato}}",
          "valor": {{valor.ToString(CultureInfo.InvariantCulture)}},
          "data_pagamento": "2026-03-01T12:00:00Z",
          "status": "{{status}}"
        }
        """;

    [Fact]
    public async Task Post_SemApiKey_Retorna401()
    {
        var resposta = await _cliente.SendAsync(ApiFactory.MontarRequisicao(Corpo("TX-401"), apiKey: null));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_ComApiKeyDesconhecida_Retorna401()
    {
        var resposta = await _cliente.SendAsync(ApiFactory.MontarRequisicao(Corpo("TX-402"), apiKey: "chave-errada"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_ComAssinaturaQueNaoBateComOCorpo_Retorna401()
    {
        var corpo = Corpo("TX-403");
        var assinaturaDeOutroCorpo = Sabemi.Pagamentos.Application.Webhooks.CalculadoraAssinatura
            .Calcular(Corpo("TX-OUTRO"), ApiFactory.Segredo);

        var resposta = await _cliente.SendAsync(
            ApiFactory.MontarRequisicao(corpo, assinatura: assinaturaDeOutroCorpo));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_ComPayloadValido_Retorna202EOEventoEProcessadoEmBackground()
    {
        var resposta = await _cliente.SendAsync(ApiFactory.MontarRequisicao(Corpo("TX-202", "CT-202", 180.75m)));

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var corpo = await LerAsync(resposta);
        corpo.GetProperty("resultado").GetString().Should().Be("Aceito");
        corpo.GetProperty("id_transacao").GetString().Should().Be("TX-202");

        var evento = await AguardarConclusaoAsync("TX-202");
        evento.Status.Should().Be("Processado");
        evento.Resultado.Should().Be("Sucesso");
        evento.Valor.Should().Be(180.75m);

        var contratos = await _cliente.GetFromJsonAsync<PagedResult<ContratoResumo>>("/api/contratos?idContrato=CT-202");
        contratos!.Itens.Should().ContainSingle();
        contratos.Itens[0].ValorTotalPago.Should().Be(180.75m);
    }

    [Fact]
    public async Task Post_MesmaTransacaoDuasVezes_Retorna200NaSegundaESoRegistraUmEvento()
    {
        var corpo = Corpo("TX-DUP", "CT-DUP", 90m);

        var primeira = await _cliente.SendAsync(ApiFactory.MontarRequisicao(corpo));
        primeira.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await AguardarConclusaoAsync("TX-DUP");

        var segunda = await _cliente.SendAsync(ApiFactory.MontarRequisicao(corpo));
        segunda.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LerAsync(segunda)).GetProperty("resultado").GetString().Should().Be("Duplicado");

        var eventos = await _cliente.GetFromJsonAsync<PagedResult<EventoResumo>>("/api/eventos?idTransacao=TX-DUP");
        eventos!.TotalItens.Should().Be(1);

        // O ponto do requisito: reenvio nao pode contar o pagamento de novo.
        var contratos = await _cliente.GetFromJsonAsync<PagedResult<ContratoResumo>>("/api/contratos?idContrato=CT-DUP");
        contratos!.Itens[0].ValorTotalPago.Should().Be(90m);
        contratos.Itens[0].QuantidadePagamentos.Should().Be(1);
    }

    [Fact]
    public async Task Post_ComEntregasSimultaneasDaMesmaTransacao_ProcessaUmaUnicaVez()
    {
        const string idTransacao = "TX-CORRIDA";
        var corpo = Corpo(idTransacao, "CT-CORRIDA", 40m);

        var respostas = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => _cliente.SendAsync(ApiFactory.MontarRequisicao(corpo))));

        respostas.Count(r => r.StatusCode == HttpStatusCode.Accepted).Should().Be(1);
        respostas.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(5);

        await AguardarConclusaoAsync(idTransacao);

        var contratos = await _cliente.GetFromJsonAsync<PagedResult<ContratoResumo>>("/api/contratos?idContrato=CT-CORRIDA");
        contratos!.Itens[0].ValorTotalPago.Should().Be(40m);
        contratos.Itens[0].QuantidadePagamentos.Should().Be(1);
    }

    [Fact]
    public async Task Post_ComPayloadInvalido_Retorna400EOEventoFicaVisivelNoPainel()
    {
        var corpoInvalido = """
        {
          "id_transacao": "TX-INVALIDO",
          "id_contrato": "",
          "valor": -10,
          "status": "NAO-EXISTE"
        }
        """;

        var resposta = await _cliente.SendAsync(ApiFactory.MontarRequisicao(corpoInvalido));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var corpo = await LerAsync(resposta);
        corpo.GetProperty("resultado").GetString().Should().Be("Invalido");
        corpo.GetProperty("erros").EnumerateObject().Should().NotBeEmpty();

        var eventos = await _cliente.GetFromJsonAsync<PagedResult<EventoResumo>>("/api/eventos?resultado=Erro");
        var evento = eventos!.Itens.Should().ContainSingle(e => e.IdTransacao == "TX-INVALIDO").Subject;
        evento.Status.Should().Be("Invalido");
        evento.MotivoFalha.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Post_ComJsonQuebrado_Retorna400ERegistraOCorpoOriginal()
    {
        const string quebrado = "{ isso nao e json valido";

        var resposta = await _cliente.SendAsync(ApiFactory.MontarRequisicao(quebrado));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var eventoId = (await LerAsync(resposta)).GetProperty("evento_id").GetGuid();
        var detalhe = await _cliente.GetFromJsonAsync<EventoDetalhe>($"/api/eventos/{eventoId}");

        detalhe!.PayloadBruto.Should().Be(quebrado);
        detalhe.Resumo.Status.Should().Be("Invalido");
    }

    [Fact]
    public async Task Post_ComEstornoAcimaDoSaldo_MarcaFalhaDeProcessamentoENaoDeValidacao()
    {
        var resposta = await _cliente.SendAsync(
            ApiFactory.MontarRequisicao(Corpo("TX-ESTORNO", "CT-ESTORNO", 500m, "ESTORNADO")));

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var evento = await AguardarConclusaoAsync("TX-ESTORNO");

        evento.Status.Should().Be("Falha");
        evento.Resultado.Should().Be("Erro");
        evento.MotivoFalha.Should().Contain("excede o saldo liquido");
        evento.Tentativas.Should().Be(1);
    }

    [Fact]
    public async Task Post_ComValorComoTexto_EAceito()
    {
        var corpo = """
        {
          "id_transacao": "TX-TEXTO",
          "id_contrato": "CT-TEXTO",
          "valor": "125.90",
          "data_pagamento": "2026-03-01T12:00:00Z",
          "status": "pago"
        }
        """;

        var resposta = await _cliente.SendAsync(ApiFactory.MontarRequisicao(corpo));

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await AguardarConclusaoAsync("TX-TEXTO")).Valor.Should().Be(125.90m);
    }

    private static async Task<JsonElement> LerAsync(HttpResponseMessage resposta)
        => JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;

    /// <summary>
    /// O processamento e assincrono por design, entao o teste espera pelo estado
    /// final em vez de assumir que ja aconteceu. O limite evita travar a suite
    /// caso o worker nunca conclua.
    /// </summary>
    private async Task<EventoResumo> AguardarConclusaoAsync(string idTransacao, int tentativas = 100)
    {
        for (var i = 0; i < tentativas; i++)
        {
            var pagina = await _cliente.GetFromJsonAsync<PagedResult<EventoResumo>>(
                $"/api/eventos?idTransacao={idTransacao}");

            var evento = pagina?.Itens.FirstOrDefault(e => e.IdTransacao == idTransacao);
            if (evento is not null && evento.Status is "Processado" or "Falha" or "Invalido")
            {
                return evento;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"O evento {idTransacao} nao concluiu o processamento a tempo.");
    }
}
