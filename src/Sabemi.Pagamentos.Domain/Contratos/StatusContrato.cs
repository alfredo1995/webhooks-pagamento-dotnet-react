using Sabemi.Pagamentos.Domain.Common;
using Sabemi.Pagamentos.Domain.Eventos;

namespace Sabemi.Pagamentos.Domain.Contratos;

/// <summary>
/// Estado consolidado de um contrato: o resultado da aplicacao dos eventos.
/// </summary>
/// <remarks>
/// Enquanto o log de eventos e imutavel e cresce, esta tabela e a leitura rapida
/// que o negocio consulta. O saldo so muda com pagamento confirmado ou estorno;
/// notificacoes de pendencia ou falha atualizam apenas o ultimo status, sem
/// mexer no valor acumulado.
/// </remarks>
public class StatusContrato : Entity, IAggregateRoot
{
    /// <summary>Construtor exigido pelo EF Core.</summary>
    private StatusContrato()
    {
        IdContrato = null!;
        UltimaTransacao = null!;
    }

    private StatusContrato(string idContrato)
    {
        GerarIdentidade();

        IdContrato = idContrato;
        UltimaTransacao = string.Empty;
        CriadoEmUtc = DateTime.UtcNow;
        AtualizadoEmUtc = DateTime.UtcNow;
    }

    public string IdContrato { get; private set; }

    public decimal ValorTotalPago { get; private set; }

    public decimal ValorTotalEstornado { get; private set; }

    public int QuantidadePagamentos { get; private set; }

    public int QuantidadeEstornos { get; private set; }

    public StatusPagamento UltimoStatus { get; private set; }

    public string UltimaTransacao { get; private set; }

    public DateTime? UltimoPagamentoUtc { get; private set; }

    public DateTime CriadoEmUtc { get; private set; }

    public DateTime AtualizadoEmUtc { get; private set; }

    /// <summary>Saldo liquido reconhecido para o contrato.</summary>
    public decimal SaldoLiquido => ValorTotalPago - ValorTotalEstornado;

    public static StatusContrato Novo(string? idContrato)
    {
        if (string.IsNullOrWhiteSpace(idContrato))
        {
            throw new DomainException("id_contrato e obrigatorio.");
        }

        return new StatusContrato(idContrato.Trim());
    }

    public void AplicarPagamento(string idTransacao, decimal valor, DateTime dataPagamento, StatusPagamento status)
    {
        if (string.IsNullOrWhiteSpace(idTransacao))
        {
            throw new DomainException("id_transacao e obrigatorio.");
        }

        if (valor <= 0)
        {
            throw new DomainException("valor deve ser maior que zero.");
        }

        var montante = decimal.Round(valor, 2, MidpointRounding.AwayFromZero);

        switch (status)
        {
            case StatusPagamento.Confirmado:
                ValorTotalPago += montante;
                QuantidadePagamentos++;
                UltimoPagamentoUtc = dataPagamento;
                break;

            case StatusPagamento.Estornado:
                if (montante > ValorTotalPago - ValorTotalEstornado)
                {
                    throw new DomainException(
                        $"Estorno de {montante:F2} excede o saldo liquido de {SaldoLiquido:F2} do contrato {IdContrato}.");
                }

                ValorTotalEstornado += montante;
                QuantidadeEstornos++;
                break;

            case StatusPagamento.Pendente:
            case StatusPagamento.Falha:
                // Nao movimenta saldo: apenas registra a ultima informacao recebida.
                break;

            default:
                throw new DomainException($"Status de pagamento nao suportado: {status}.");
        }

        UltimoStatus = status;
        UltimaTransacao = idTransacao.Trim();
        AtualizadoEmUtc = DateTime.UtcNow;
    }
}
