using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sabemi.Pagamentos.Application.Consultas;
using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Api.Controllers;

/// <summary>Status consolidado por contrato.</summary>
[ApiController]
[Route("api/contratos")]
[Produces("application/json")]
[Authorize]
public sealed class ContratosController(IConsultaService consultas) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ContratoResumo>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ContratoResumo>>> Buscar(
        [FromQuery] string? idContrato,
        [FromQuery] int? pagina,
        [FromQuery] int? tamanhoPagina,
        CancellationToken cancellationToken)
        => Ok(await consultas.BuscarContratosAsync(idContrato, pagina, tamanhoPagina, cancellationToken));
}
