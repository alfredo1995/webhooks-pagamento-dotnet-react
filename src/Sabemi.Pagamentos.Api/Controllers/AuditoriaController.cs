using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sabemi.Pagamentos.Api.Seguranca;
using Sabemi.Pagamentos.Application.Consultas;
using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Api.Controllers;

/// <summary>
/// Trilha de auditoria: quem consultou o que no painel.
/// </summary>
/// <remarks>
/// Restrita a administrador. Nao e zelo excessivo: a trilha guarda os filtros
/// usados nas consultas, entao ler a trilha e uma forma indireta de saber quais
/// contratos foram investigados e por quem.
/// </remarks>
[ApiController]
[Route("api/auditoria")]
[Produces("application/json")]
[Authorize(Roles = OpcoesAutenticacao.PapelAdministrador)]
public sealed class AuditoriaController(IConsultaService consultas) : ControllerBase
{
    /// <summary>Lista os acessos registrados, do mais recente para o mais antigo.</summary>
    /// <param name="usuario">Filtro parcial pelo login de quem acessou.</param>
    /// <param name="recurso">Filtro parcial pela rota consultada.</param>
    /// <param name="pagina">Pagina, iniciando em 1.</param>
    /// <param name="tamanhoPagina">Itens por pagina (maximo 200).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuditoriaResumo>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AuditoriaResumo>>> Buscar(
        [FromQuery] string? usuario,
        [FromQuery] string? recurso,
        [FromQuery] int? pagina,
        [FromQuery] int? tamanhoPagina,
        CancellationToken cancellationToken)
        => Ok(await consultas.BuscarAuditoriaAsync(usuario, recurso, pagina, tamanhoPagina, cancellationToken));
}
