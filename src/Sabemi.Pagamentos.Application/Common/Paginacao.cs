namespace Sabemi.Pagamentos.Application.Common;

/// <summary>Normaliza a paginacao vinda da borda antes de chegar ao repositorio.</summary>
public static class Paginacao
{
    public const int TamanhoPadrao = 25;
    public const int TamanhoMaximo = 200;

    public static (int Pagina, int TamanhoPagina) Normalizar(int? pagina, int? tamanhoPagina)
    {
        var p = pagina is null or < 1 ? 1 : pagina.Value;
        var tamanho = tamanhoPagina switch
        {
            null or < 1 => TamanhoPadrao,
            > TamanhoMaximo => TamanhoMaximo,
            _ => tamanhoPagina.Value,
        };

        return (p, tamanho);
    }
}
