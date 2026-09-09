using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Api.Auditoria;
using Sabemi.Pagamentos.Api.Seguranca;

namespace Sabemi.Pagamentos.Api.Controllers;

public sealed record LoginRequest(string Login, string Senha);

public sealed record LoginResposta(string Token, DateTime ExpiraEmUtc, string Usuario, string Nome, string Papel);

public sealed record IdentidadeResposta(string Usuario, string Nome, string Papel);

/// <summary>Emissao de token para o painel administrativo.</summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AutenticacaoController(
    IServicoTokenJwt tokens,
    IOptions<OpcoesAutenticacao> opcoes) : ControllerBase
{
    /// <summary>
    /// Troca credenciais por um token de acesso.
    /// </summary>
    /// <remarks>
    /// Existe apenas quando a API esta emitindo token localmente. Com um IdP
    /// corporativo configurado, quem emite e ele, e esta rota responde
    /// <c>501</c> — nao faria sentido manter uma segunda porta de entrada aberta
    /// justo onde o SSO deveria ser o unico caminho.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingWebhook.PoliticaLogin)]
    [ProducesResponseType(typeof(LoginResposta), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public ActionResult<LoginResposta> Login([FromBody] LoginRequest requisicao)
    {
        ArgumentNullException.ThrowIfNull(requisicao);

        if (!opcoes.Value.EmissaoLocalHabilitada)
        {
            return Problem(
                statusCode: StatusCodes.Status501NotImplemented,
                title: "Login local desabilitado.",
                detail: "A API valida tokens do IdP corporativo. Autentique-se pelo SSO.");
        }

        var emitido = tokens.Autenticar(requisicao.Login, requisicao.Senha);
        if (emitido is null)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Credenciais invalidas.",
                detail: "Usuario ou senha nao conferem.");
        }

        // A trilha registra o login pelo nome do usuario, e nao como anonimo:
        // a identidade so passa a valer no proximo request, mas quem entrou e
        // exatamente a informacao que essa linha precisa guardar.
        HttpContext.Items[AuditoriaMiddleware.ItemUsuario] = emitido.Usuario;

        return Ok(new LoginResposta(
            emitido.Token,
            emitido.ExpiraEmUtc,
            emitido.Usuario,
            emitido.Nome,
            emitido.Papel));
    }

    /// <summary>Identidade do token em uso, para o painel montar o cabecalho.</summary>
    [HttpGet("eu")]
    [Authorize]
    [ProducesResponseType(typeof(IdentidadeResposta), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<IdentidadeResposta> Eu() => Ok(new IdentidadeResposta(
        User.Identity?.Name ?? "desconhecido",
        User.FindFirstValue(OpcoesAutenticacao.ClaimNome) ?? User.Identity?.Name ?? "desconhecido",
        User.FindFirstValue(OpcoesAutenticacao.ClaimPapel) ?? OpcoesAutenticacao.PapelOperador));
}
