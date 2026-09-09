namespace Sabemi.Pagamentos.Api.Auditoria;

public sealed class OpcoesAuditoria
{
    public const string Secao = "Auditoria";

    public bool Habilitada { get; set; } = true;

    /// <summary>
    /// Rotas que nao entram na trilha.
    /// </summary>
    /// <remarks>
    /// O padrao ignora <c>/api/metricas</c>, e a razao nao e volume: a trilha
    /// existe para responder "quem viu o dado de quem", e um endpoint que so
    /// devolve contagens agregadas nao expoe dado de ninguem. Registrar o polling
    /// do painel a cada tres segundos afogaria as consultas que importam.
    /// </remarks>
    public IList<string> RotasIgnoradas { get; init; } = ["/api/metricas"];
}
