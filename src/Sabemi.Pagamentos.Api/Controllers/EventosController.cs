using Microsoft.AspNetCore.Mvc;
using Sabemi.Pagamentos.Application.Consultas;
using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Api.Controllers;

/// <summary>Consulta do log de eventos brutos, usada pelo painel administrativo.</summary>
[ApiController]
[Route("api/eventos")]
[Produces("application/json")]
public sealed class EventosController(IConsultaService consultas) : ControllerBase
{
    /// <summary>Lista os eventos recebidos, do mais recente para o mais antigo.</summary>
    /// <param name="resultado">Sucesso, Erro, Pendente ou Todos.</param>
    /// <param name="idContrato">Filtro parcial por identificador do contrato.</param>
    /// <param name="idTransacao">Filtro parcial por identificador da transacao.</param>
    /// <param name="pagina">Pagina, iniciando em 1.</param>
    /// <param name="tamanhoPagina">Itens por pagina (maximo 200).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<EventoResumo>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<EventoResumo>>> Buscar(
        [FromQuery] string? resultado,
        [FromQuery] string? idContrato,
        [FromQuery] string? idTransacao,
        [FromQuery] int? pagina,
        [FromQuery] int? tamanhoPagina,
        CancellationToken cancellationToken)
        => Ok(await consultas.BuscarEventosAsync(resultado, idContrato, idTransacao, pagina, tamanhoPagina, cancellationToken));

    /// <summary>Detalhe do evento, incluindo o payload bruto exatamente como recebido.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(EventoDetalhe), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventoDetalhe>> ObterPorId(Guid id, CancellationToken cancellationToken)
        => Ok(await consultas.ObterEventoAsync(id, cancellationToken));
}
