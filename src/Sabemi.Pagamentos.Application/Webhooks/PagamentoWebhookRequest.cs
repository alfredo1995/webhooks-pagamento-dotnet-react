using System.Text.Json.Serialization;

namespace Sabemi.Pagamentos.Application.Webhooks;

/// <summary>
/// Payload enviado pelo banco parceiro. Os nomes seguem o contrato do banco
/// (snake_case) e todos os campos sao anulaveis de proposito: um campo faltando
/// deve virar erro de validacao registrado no painel, nao um 400 de desserializacao
/// que perderia o evento.
/// </summary>
public sealed class PagamentoWebhookRequest
{
    [JsonPropertyName("id_transacao")]
    public string? IdTransacao { get; init; }

    [JsonPropertyName("id_contrato")]
    public string? IdContrato { get; init; }

    [JsonPropertyName("valor")]
    public decimal? Valor { get; init; }

    [JsonPropertyName("data_pagamento")]
    public DateTime? DataPagamento { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

/// <summary>Como o recebimento terminou, do ponto de vista do banco parceiro.</summary>
public enum ResultadoRecebimento
{
    /// <summary>Payload valido, gravado e enfileirado. HTTP 202.</summary>
    Aceito = 1,

    /// <summary>id_transacao ja recebido antes; nada foi reprocessado. HTTP 200.</summary>
    Duplicado = 2,

    /// <summary>Payload gravado no log, porem reprovado na validacao. HTTP 400.</summary>
    Invalido = 3,
}

public sealed record RespostaRecebimento(
    ResultadoRecebimento Resultado,
    Guid EventoId,
    string IdTransacao,
    string Mensagem,
    IReadOnlyDictionary<string, string[]>? Erros = null);
