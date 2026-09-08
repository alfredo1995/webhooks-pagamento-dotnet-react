namespace Sabemi.Pagamentos.Domain.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Itens, int Pagina, int TamanhoPagina, int TotalItens)
{
    public int TotalPaginas => TamanhoPagina <= 0 ? 0 : (int)Math.Ceiling(TotalItens / (double)TamanhoPagina);

    public bool TemProximaPagina => Pagina < TotalPaginas;
}
