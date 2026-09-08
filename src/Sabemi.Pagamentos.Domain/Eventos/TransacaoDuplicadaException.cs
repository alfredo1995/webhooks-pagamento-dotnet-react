namespace Sabemi.Pagamentos.Domain.Eventos;

/// <summary>
/// O indice unico de <c>IdTransacao</c> recusou a gravacao.
/// </summary>
/// <remarks>
/// A infraestrutura traduz a violacao de constraint do banco para esta excecao,
/// para que a camada de aplicacao trate duplicidade como um conceito de negocio
/// e nao precise conhecer codigos de erro de um provider especifico.
/// </remarks>
public sealed class TransacaoDuplicadaException(string idTransacao, Exception? innerException = null)
    : Exception($"A transacao '{idTransacao}' ja foi registrada.", innerException)
{
    public string IdTransacao { get; } = idTransacao;
}
