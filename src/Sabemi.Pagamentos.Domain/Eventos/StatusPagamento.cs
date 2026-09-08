namespace Sabemi.Pagamentos.Domain.Eventos;

/// <summary>Status que o banco parceiro envia no campo <c>status</c> do webhook.</summary>
public enum StatusPagamento
{
    Confirmado = 1,
    Pendente = 2,
    Falha = 3,
    Estornado = 4,
}
