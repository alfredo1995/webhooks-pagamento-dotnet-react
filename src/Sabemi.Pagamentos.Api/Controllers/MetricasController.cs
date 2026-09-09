using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sabemi.Pagamentos.Application.Consultas;

namespace Sabemi.Pagamentos.Api.Controllers;

/// <summary>Numeros do topo do painel.</summary>
[ApiController]
[Route("api/metricas")]
[Produces("application/json")]
[Authorize]
public sealed class MetricasController(IConsultaService consultas) : ControllerBase
{
    /// <summary>Totais por resultado e quantidade de eventos ainda na fila.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(Metricas), StatusCodes.Status200OK)]
    public async Task<ActionResult<Metricas>> Obter(CancellationToken cancellationToken)
        => Ok(await consultas.ObterMetricasAsync(cancellationToken));
}
