using FluentValidation;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.Application.Webhooks;

public sealed class PagamentoWebhookValidator : AbstractValidator<PagamentoWebhookRequest>
{
    /// <summary>Tolerancia para relogio adiantado do parceiro.</summary>
    private static readonly TimeSpan FolgaFutura = TimeSpan.FromHours(24);

    public PagamentoWebhookValidator()
    {
        RuleFor(x => x.IdTransacao)
            .NotEmpty().WithMessage("id_transacao e obrigatorio.")
            .MaximumLength(EventoWebhook.TamanhoMaximoIdTransacao);

        RuleFor(x => x.IdContrato)
            .NotEmpty().WithMessage("id_contrato e obrigatorio.")
            .MaximumLength(EventoWebhook.TamanhoMaximoIdContrato);

        RuleFor(x => x.Valor)
            .NotNull().WithMessage("valor e obrigatorio.")
            .GreaterThan(0).WithMessage("valor deve ser maior que zero.")
            .LessThanOrEqualTo(10_000_000m).WithMessage("valor acima do teto aceito para uma notificacao.");

        RuleFor(x => x.DataPagamento)
            .NotNull().WithMessage("data_pagamento e obrigatorio.")
            // O campo ausente ja e reportado pelo NotNull; aqui o null precisa
            // passar reto, senao a propria validacao quebraria com o payload que
            // ela deveria reprovar.
            .Must(data => data is null || data.Value.ToUniversalTime() <= DateTime.UtcNow.Add(FolgaFutura))
            .WithMessage("data_pagamento nao pode estar no futuro.");

        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("status e obrigatorio.")
            .Must(status => StatusPagamentoParser.TentarConverter(status, out _))
            .WithMessage($"status deve ser um entre: {StatusPagamentoParser.ValoresAceitos}.");
    }
}

/// <summary>
/// Converte o <c>status</c> textual do banco para o enum do dominio, aceitando as
/// variacoes que aparecem na pratica (maiusculas, acentos, sinonimos).
/// </summary>
public static class StatusPagamentoParser
{
    private static readonly Dictionary<string, StatusPagamento> Mapa = new(StringComparer.OrdinalIgnoreCase)
    {
        ["confirmado"] = StatusPagamento.Confirmado,
        ["confirmed"] = StatusPagamento.Confirmado,
        ["pago"] = StatusPagamento.Confirmado,
        ["liquidado"] = StatusPagamento.Confirmado,
        ["sucesso"] = StatusPagamento.Confirmado,
        ["pendente"] = StatusPagamento.Pendente,
        ["pending"] = StatusPagamento.Pendente,
        ["aguardando"] = StatusPagamento.Pendente,
        ["falha"] = StatusPagamento.Falha,
        ["falhou"] = StatusPagamento.Falha,
        ["erro"] = StatusPagamento.Falha,
        ["failed"] = StatusPagamento.Falha,
        ["recusado"] = StatusPagamento.Falha,
        ["estornado"] = StatusPagamento.Estornado,
        ["estorno"] = StatusPagamento.Estornado,
        ["refunded"] = StatusPagamento.Estornado,
        ["chargeback"] = StatusPagamento.Estornado,
    };

    public static string ValoresAceitos => "CONFIRMADO, PENDENTE, FALHA, ESTORNADO";

    public static bool TentarConverter(string? valor, out StatusPagamento status)
    {
        status = default;

        return !string.IsNullOrWhiteSpace(valor) && Mapa.TryGetValue(valor.Trim(), out status);
    }
}
