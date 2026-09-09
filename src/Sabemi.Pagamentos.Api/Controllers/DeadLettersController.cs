using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sabemi.Pagamentos.Api.Seguranca;
using Sabemi.Pagamentos.Application.Consultas;
using Sabemi.Pagamentos.Application.DeadLetters;
using Sabemi.Pagamentos.Domain.Common;

namespace Sabemi.Pagamentos.Api.Controllers;

/// <summary>Eventos que esgotaram as retentativas automaticas.</summary>
[ApiController]
[Route("api/dead-letters")]
[Produces("application/json")]
[Authorize]
public sealed class DeadLettersController(
    IConsultaService consultas,
    IReprocessamentoService reprocessamento) : ControllerBase
{
    /// <summary>Lista as cartas da DLQ, das mais recentes para as mais antigas.</summary>
    /// <param name="apenasPendentes">Oculta as que ja foram reprocessadas.</param>
    /// <param name="pagina">Pagina, iniciando em 1.</param>
    /// <param name="tamanhoPagina">Itens por pagina (maximo 200).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<DeadLetterResumo>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<DeadLetterResumo>>> Buscar(
        [FromQuery] bool apenasPendentes,
        [FromQuery] int? pagina,
        [FromQuery] int? tamanhoPagina,
        CancellationToken cancellationToken)
        => Ok(await consultas.BuscarDeadLettersAsync(apenasPendentes, pagina, tamanhoPagina, cancellationToken));

    /// <summary>
    /// Devolve o evento para a fila com um orcamento novo de tentativas.
    /// </summary>
    /// <remarks>
    /// Restrito a administrador: reprocessar movimenta saldo de contrato, o que
    /// e uma acao de escrita disfarcada de botao de tela. Fica registrado na
    /// trilha de auditoria com o usuario que disparou.
    /// </remarks>
    [HttpPost("{eventoId:guid}/reprocessar")]
    [Authorize(Roles = OpcoesAutenticacao.PapelAdministrador)]
    [ProducesResponseType(typeof(ReprocessamentoAceito), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReprocessamentoAceito>> Reprocessar(
        Guid eventoId,
        CancellationToken cancellationToken)
    {
        var resultado = await reprocessamento.ReprocessarAsync(
            eventoId,
            User.Identity?.Name ?? "desconhecido",
            cancellationToken);

        return Accepted($"/api/eventos/{resultado.EventoId}", resultado);
    }
}
