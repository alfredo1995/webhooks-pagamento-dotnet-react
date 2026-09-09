using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Sabemi.Pagamentos.Application.Consultas;
using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.IntegrationTests;

/// <summary>
/// Fabrica com duas tentativas e backoff de um segundo: curto o bastante para o
/// teste rodar, longo o bastante para que a retentativa seja mesmo observavel.
/// </summary>
public sealed class ApiFactoryComRetentativa : ApiFactory
{
    protected override void Ajustar(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("Processamento:Retentativa:MaximoTentativas", "2");
        builder.UseSetting("Processamento:Retentativa:BackoffBaseSegundos", "1");
        builder.UseSetting("Processamento:Retentativa:BackoffMaximoSegundos", "1");
        builder.UseSetting("Processamento:IntervaloSupervisorSegundos", "1");
    }
}

/// <summary>
/// A jornada completa de uma falha de processamento: retentativa automatica,
/// dead-letter queue e volta para a fila por decisao humana.
/// </summary>
public class RetentativaEDeadLetterTests(ApiFactoryComRetentativa fabrica)
    : IClassFixture<ApiFactoryComRetentativa>
{
    private readonly HttpClient _admin = fabrica.CriarClienteDoPainel(ApiFactory.Administrador);

    private static string Corpo(string idTransacao, string idContrato, decimal valor, string status)
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
    public async Task EventoQueFalha_ERetentadoAutomaticamenteEDepoisVaiParaADeadLetterQueue()
    {
        // Estorno sem saldo: falha de processamento, nao de validacao.
        var resposta = await _admin.SendAsync(
            ApiFactory.MontarRequisicao(Corpo("TX-RETRY", "CT-RETRY", 500m, "ESTORNADO")));

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var emRetentativa = await AguardarStatusAsync("TX-RETRY", "AguardandoRetentativa");
        emRetentativa.Tentativas.Should().Be(1);
        emRetentativa.ProximaTentativaEmUtc.Should().NotBeNull();
        emRetentativa.Resultado.Should().Be("Pendente");

        // O supervisor devolve o evento para a fila quando o horario chega; a
        // segunda tentativa esgota o orcamento.
        var morto = await AguardarStatusAsync("TX-RETRY", "Falha");
        morto.Tentativas.Should().Be(2);
        morto.Resultado.Should().Be("Erro");

        var dlq = await _admin.GetFromJsonAsync<PagedResult<DeadLetterResumo>>(
            "/api/dead-letters?apenasPendentes=true");

        var carta = dlq!.Itens.Should().ContainSingle(c => c.IdTransacao == "TX-RETRY").Subject;
        carta.Tentativas.Should().Be(2);
        carta.Motivo.Should().Contain("excede o saldo liquido");
        carta.Pendente.Should().BeTrue();
    }

    [Fact]
    public async Task Reprocessar_ComACausaCorrigida_TiraOEventoDaDlqEConcluiComSucesso()
    {
        await _admin.SendAsync(ApiFactory.MontarRequisicao(Corpo("TX-DLQ", "CT-DLQ", 300m, "ESTORNADO")));

        var morto = await AguardarStatusAsync("TX-DLQ", "Falha");

        // A causa era ausencia de saldo: um pagamento confirmado a resolve.
        await _admin.SendAsync(ApiFactory.MontarRequisicao(Corpo("TX-DLQ-PAG", "CT-DLQ", 300m, "CONFIRMADO")));
        await AguardarStatusAsync("TX-DLQ-PAG", "Processado");

        var reprocessamento = await _admin.PostAsync(
            new Uri($"/api/dead-letters/{morto.Id}/reprocessar", UriKind.Relative),
            content: null);

        reprocessamento.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var concluido = await AguardarStatusAsync("TX-DLQ", "Processado");
        concluido.Tentativas.Should().Be(1, "o reprocessamento manual comeca com um orcamento novo");

        var contratos = await _admin.GetFromJsonAsync<PagedResult<ContratoResumo>>("/api/contratos?idContrato=CT-DLQ");
        contratos!.Itens[0].SaldoLiquido.Should().Be(0m);

        var pendentes = await _admin.GetFromJsonAsync<PagedResult<DeadLetterResumo>>(
            "/api/dead-letters?apenasPendentes=true");
        pendentes!.Itens.Should().NotContain(c => c.IdTransacao == "TX-DLQ");

        var historico = await _admin.GetFromJsonAsync<PagedResult<DeadLetterResumo>>("/api/dead-letters");
        var carta = historico!.Itens.Should().ContainSingle(c => c.IdTransacao == "TX-DLQ").Subject;
        carta.ReprocessadoPor.Should().Be(ApiFactory.Administrador);
    }

    [Fact]
    public async Task Reprocessar_ComPapelOperador_Retorna403()
    {
        var operador = fabrica.CriarClienteDoPainel(ApiFactory.Operador);

        var resposta = await operador.PostAsync(
            new Uri($"/api/dead-letters/{Guid.NewGuid()}/reprocessar", UriKind.Relative),
            content: null);

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<EventoResumo> AguardarStatusAsync(string idTransacao, string status, int tentativas = 150)
    {
        for (var i = 0; i < tentativas; i++)
        {
            var pagina = await _admin.GetFromJsonAsync<PagedResult<EventoResumo>>(
                $"/api/eventos?idTransacao={idTransacao}");

            var evento = pagina?.Itens.FirstOrDefault(e => e.IdTransacao == idTransacao);
            if (evento?.Status == status)
            {
                return evento;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"O evento {idTransacao} nao chegou ao status {status} a tempo.");
    }
}
