using Sabemi.Pagamentos.Domain.Auditoria;
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
    DateTime? ProximaTentativaEmUtc,
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

public sealed record DeadLetterResumo(
    Guid Id,
    Guid EventoId,
    string IdTransacao,
    string? IdContrato,
    string Motivo,
    int Tentativas,
    DateTime CriadoEmUtc,
    DateTime? ReprocessadoEmUtc,
    string? ReprocessadoPor,
    bool Pendente);

public sealed record AuditoriaResumo(
    Guid Id,
    string Usuario,
    string Papel,
    string Metodo,
    string Recurso,
    string? Consulta,
    string IpOrigem,
    int StatusHttp,
    string? TraceId,
    DateTime EmUtc);

public sealed record Metricas(
    int Total,
    int Pendentes,
    int Sucesso,
    int Erro,
    int EmRetentativa,
    int NaFila,
    int OutboxPendente,
    int DeadLetters,
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
            evento.ProximaTentativaEmUtc,
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

    public static DeadLetterResumo ParaResumo(this DeadLetter carta)
    {
        ArgumentNullException.ThrowIfNull(carta);

        return new DeadLetterResumo(
            carta.Id,
            carta.EventoId,
            carta.IdTransacao,
            carta.IdContrato,
            carta.Motivo,
            carta.Tentativas,
            carta.CriadoEmUtc,
            carta.ReprocessadoEmUtc,
            carta.ReprocessadoPor,
            carta.Pendente);
    }

    public static AuditoriaResumo ParaResumo(this RegistroAuditoria registro)
    {
        ArgumentNullException.ThrowIfNull(registro);

        return new AuditoriaResumo(
            registro.Id,
            registro.Usuario,
            registro.Papel,
            registro.Metodo,
            registro.Recurso,
            registro.Consulta,
            registro.IpOrigem,
            registro.StatusHttp,
            registro.TraceId,
            registro.EmUtc);
    }
}
