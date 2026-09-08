using Sabemi.Pagamentos.Domain.Contratos;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.Application.Consultas;

public sealed record EventoResumo(
    Guid Id,
    string IdTransacao,
    string? IdContrato,
    decimal? Valor,
    DateTime? DataPagamento,
    string? StatusPagamento,
    string Status,
    string Resultado,
    string? MotivoFalha,
    int Tentativas,
    string OrigemParceiro,
    DateTime RecebidoEmUtc,
    DateTime? ProcessadoEmUtc,
    long? DuracaoProcessamentoMs);

public sealed record EventoDetalhe(EventoResumo Resumo, string PayloadBruto);

public sealed record ContratoResumo(
    string IdContrato,
    decimal ValorTotalPago,
    decimal ValorTotalEstornado,
    decimal SaldoLiquido,
    int QuantidadePagamentos,
    int QuantidadeEstornos,
    string UltimoStatus,
    string UltimaTransacao,
    DateTime? UltimoPagamentoUtc,
    DateTime AtualizadoEmUtc);

public sealed record Metricas(
    int Total,
    int Pendentes,
    int Sucesso,
    int Erro,
    int NaFila,
    IReadOnlyDictionary<string, int> PorStatus);

public static class ConsultaMapper
{
    public static EventoResumo ParaResumo(this EventoWebhook evento)
    {
        ArgumentNullException.ThrowIfNull(evento);

        return new EventoResumo(
            evento.Id,
            evento.IdTransacao,
            evento.IdContrato,
            evento.Valor,
            evento.DataPagamento,
            evento.StatusPagamento?.ToString(),
            evento.Status.ToString(),
            evento.Status.ParaResultado().ToString(),
            evento.MotivoFalha,
            evento.Tentativas,
            evento.OrigemParceiro,
            evento.RecebidoEmUtc,
            evento.ProcessadoEmUtc,
            evento.DuracaoProcessamentoMs);
    }

    public static ContratoResumo ParaResumo(this StatusContrato contrato)
    {
        ArgumentNullException.ThrowIfNull(contrato);

        return new ContratoResumo(
            contrato.IdContrato,
            contrato.ValorTotalPago,
            contrato.ValorTotalEstornado,
            contrato.SaldoLiquido,
            contrato.QuantidadePagamentos,
            contrato.QuantidadeEstornos,
            contrato.UltimoStatus.ToString(),
            contrato.UltimaTransacao,
            contrato.UltimoPagamentoUtc,
            contrato.AtualizadoEmUtc);
    }
}
