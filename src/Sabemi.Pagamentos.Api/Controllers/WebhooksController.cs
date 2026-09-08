using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Sabemi.Pagamentos.Api.Webhooks;
using Sabemi.Pagamentos.Application.Webhooks;

namespace Sabemi.Pagamentos.Api.Controllers;

/// <summary>Recebimento das notificacoes de pagamento do banco parceiro.</summary>
[ApiController]
[Route("webhooks")]
[Produces("application/json")]
public sealed class WebhooksController(IRecebimentoWebhookService recebimento) : ControllerBase
{
    /// <summary>
    /// Opcoes proprias porque o contrato e do banco, nao nosso: snake_case e
    /// tolerancia a numero enviado como string.
    /// </summary>
    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Recebe a confirmacao de liquidacao de um seguro ou parcela de emprestimo.
    /// </summary>
    /// <remarks>
    /// Responde imediatamente: a regra de negocio roda em background. Os codigos
    /// sao <c>202</c> para evento aceito e enfileirado, <c>200</c> quando a mesma
    /// <c>id_transacao</c> ja havia chegado (nada e reprocessado), <c>400</c>
    /// quando o payload e invalido — e nesse caso o evento fica gravado no log e
    /// aparece no painel — e <c>401</c> quando ApiKey ou assinatura nao conferem.
    /// </remarks>
    [HttpPost("pagamento")]
    [ServiceFilter(typeof(AutenticacaoWebhookFilter))]
    [ProducesResponseType(typeof(RespostaWebhook), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(RespostaWebhook), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(RespostaWebhook), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Pagamento(CancellationToken cancellationToken)
    {
        var corpo = HttpContext.Items[AutenticacaoWebhookFilter.ItemPayloadBruto] as string ?? string.Empty;
        var parceiro = HttpContext.Items[AutenticacaoWebhookFilter.ItemParceiro] as string ?? "desconhecido";

        var payload = Desserializar(corpo);

        var resposta = await recebimento.ReceberAsync(payload, corpo, parceiro, cancellationToken);

        var corpoResposta = new RespostaWebhook(
            resposta.Resultado.ToString(),
            resposta.EventoId,
            resposta.IdTransacao,
            resposta.Mensagem,
            resposta.Erros);

        return resposta.Resultado switch
        {
            ResultadoRecebimento.Aceito => Accepted($"/api/eventos/{resposta.EventoId}", corpoResposta),
            ResultadoRecebimento.Duplicado => Ok(corpoResposta),
            _ => BadRequest(corpoResposta),
        };
    }

    /// <summary>
    /// JSON quebrado nao pode derrubar a requisicao: vira payload nulo e segue
    /// para o servico, que registra o evento como invalido e o expoe no painel.
    /// </summary>
    private static PagamentoWebhookRequest? Desserializar(string corpo)
    {
        if (string.IsNullOrWhiteSpace(corpo))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PagamentoWebhookRequest>(corpo, OpcoesJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Resposta devolvida ao banco parceiro, em snake_case como o contrato dele.</summary>
public sealed record RespostaWebhook(
    [property: JsonPropertyName("resultado")] string Resultado,
    [property: JsonPropertyName("evento_id")] Guid EventoId,
    [property: JsonPropertyName("id_transacao")] string IdTransacao,
    [property: JsonPropertyName("mensagem")] string Mensagem,
    [property: JsonPropertyName("erros")] IReadOnlyDictionary<string, string[]>? Erros);
