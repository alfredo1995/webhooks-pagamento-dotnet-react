using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Sabemi.Pagamentos.Application.Webhooks;

namespace Sabemi.Pagamentos.IntegrationTests;

/// <summary>Parceiro com teto baixo, para que o limite seja alcancavel no teste.</summary>
public sealed class ApiFactoryComLimiteBaixo : ApiFactory
{
    public const int Limite = 3;

    protected override void Ajustar(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting(
            "Webhook:Parceiros:0:LimitePorMinuto",
            Limite.ToString(CultureInfo.InvariantCulture));
    }
}

public class RateLimitPorParceiroTests(ApiFactoryComLimiteBaixo fabrica)
    : IClassFixture<ApiFactoryComLimiteBaixo>
{
    private readonly HttpClient _cliente = fabrica.CriarClienteAnonimo();

    [Fact]
    public async Task Post_AcimaDoTetoDoParceiro_Retorna429ComRetryAfter()
    {
        var respostas = new List<HttpResponseMessage>();

        for (var i = 0; i < ApiFactoryComLimiteBaixo.Limite + 1; i++)
        {
            respostas.Add(await _cliente.SendAsync(ApiFactory.MontarRequisicao(Corpo($"TX-RL-{i}"))));
        }

        respostas.Take(ApiFactoryComLimiteBaixo.Limite)
            .Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Accepted);

        var recusada = respostas[^1];
        recusada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        recusada.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Post_ComApiKeyDesconhecida_CaiNaParticaoRestritaENaoConsomeOBaldeDoParceiro()
    {
        // Chaves aleatorias compartilham um unico balde: uma varredura nao ganha
        // um limite novo a cada tentativa.
        var resposta = await _cliente.SendAsync(
            ApiFactory.MontarRequisicao(Corpo("TX-RL-X"), apiKey: $"chave-{Guid.NewGuid():N}"));

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string Corpo(string idTransacao) => $$"""
        {
          "id_transacao": "{{idTransacao}}",
          "id_contrato": "CT-RL",
          "valor": 10.00,
          "data_pagamento": "2026-03-01T12:00:00Z",
          "status": "CONFIRMADO"
        }
        """;
}

/// <summary>
/// Um parceiro no meio da rotacao (janela aberta) e outro com a janela ja
/// fechada, para provar que a aceitacao dupla tem prazo.
/// </summary>
public sealed class ApiFactoryComRotacao : ApiFactory
{
    public const string ApiKeyNova = "chave-nova";
    public const string SegredoNovo = "segredo-novo";
    public const string ApiKeyAntiga = "chave-antiga";
    public const string SegredoAntigo = "segredo-antigo";

    public const string ApiKeyExpirada = "chave-expirada";
    public const string SegredoExpirado = "segredo-expirado";
    public const string ApiKeyVigenteDoExpirado = "chave-vigente-2";
    public const string SegredoVigenteDoExpirado = "segredo-vigente-2";

    protected override void Ajustar(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("Webhook:Parceiros:0:ApiKey", ApiKeyNova);
        builder.UseSetting("Webhook:Parceiros:0:Segredo", SegredoNovo);
        builder.UseSetting("Webhook:Parceiros:0:ApiKeyAnterior", ApiKeyAntiga);
        builder.UseSetting("Webhook:Parceiros:0:SegredoAnterior", SegredoAntigo);
        builder.UseSetting("Webhook:Parceiros:0:ChavesAnterioresValidasAte", "2999-01-01T00:00:00Z");

        builder.UseSetting("Webhook:Parceiros:1:Nome", "parceiro-com-janela-fechada");
        builder.UseSetting("Webhook:Parceiros:1:ApiKey", ApiKeyVigenteDoExpirado);
        builder.UseSetting("Webhook:Parceiros:1:Segredo", SegredoVigenteDoExpirado);
        builder.UseSetting("Webhook:Parceiros:1:ApiKeyAnterior", ApiKeyExpirada);
        builder.UseSetting("Webhook:Parceiros:1:SegredoAnterior", SegredoExpirado);
        builder.UseSetting("Webhook:Parceiros:1:ChavesAnterioresValidasAte", "2020-01-01T00:00:00Z");
        builder.UseSetting("Webhook:Parceiros:1:LimitePorMinuto", "1000");
    }
}

public class RotacaoDeSegredoTests(ApiFactoryComRotacao fabrica) : IClassFixture<ApiFactoryComRotacao>
{
    private readonly HttpClient _cliente = fabrica.CriarClienteAnonimo();

    [Fact]
    public async Task Post_ComAsChavesNovas_EAceito()
    {
        var resposta = await Enviar(
            "TX-ROT-NOVA",
            ApiFactoryComRotacao.ApiKeyNova,
            ApiFactoryComRotacao.SegredoNovo);

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Post_ComAsChavesAnterioresDentroDaJanela_ContinuaAceito()
    {
        // O parceiro ainda nao migrou; a rotacao nao pode derrubar a integracao.
        var resposta = await Enviar(
            "TX-ROT-ANTIGA",
            ApiFactoryComRotacao.ApiKeyAntiga,
            ApiFactoryComRotacao.SegredoAntigo);

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Post_MisturandoChaveNovaComSegredoAntigo_EAceitoDentroDaJanela()
    {
        // ApiKey e segredo sao rotacionados de forma independente: o parceiro
        // pode ter trocado um e nao o outro.
        var resposta = await Enviar(
            "TX-ROT-MISTA",
            ApiFactoryComRotacao.ApiKeyNova,
            ApiFactoryComRotacao.SegredoAntigo);

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Post_ComChavesAnterioresForaDaJanela_Retorna401()
    {
        // A janela fechada e o que impede que "aceitar as duas" vire permanente.
        var resposta = await Enviar(
            "TX-ROT-EXPIRADA",
            ApiFactoryComRotacao.ApiKeyExpirada,
            ApiFactoryComRotacao.SegredoExpirado);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_ComSegredoAntigoDeOutroParceiro_Retorna401()
    {
        var resposta = await Enviar(
            "TX-ROT-CRUZADA",
            ApiFactoryComRotacao.ApiKeyNova,
            ApiFactoryComRotacao.SegredoExpirado);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private Task<HttpResponseMessage> Enviar(string idTransacao, string apiKey, string segredo)
    {
        var corpo = $$"""
            {
              "id_transacao": "{{idTransacao}}",
              "id_contrato": "CT-ROT",
              "valor": 15.00,
              "data_pagamento": "2026-03-01T12:00:00Z",
              "status": "CONFIRMADO"
            }
            """;

        return _cliente.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/webhooks/pagamento")
        {
            Content = new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"),
            Headers =
            {
                { "X-Api-Key", apiKey },
                { "X-Signature", CalculadoraAssinatura.Calcular(corpo, segredo) },
            },
        });
    }
}
