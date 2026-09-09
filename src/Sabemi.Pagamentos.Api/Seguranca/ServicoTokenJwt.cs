using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Sabemi.Pagamentos.Api.Seguranca;

public sealed record TokenEmitido(string Token, DateTime ExpiraEmUtc, string Usuario, string Nome, string Papel);

public interface IServicoTokenJwt
{
    /// <summary>Devolve o token ou <c>null</c> quando as credenciais nao conferem.</summary>
    TokenEmitido? Autenticar(string? login, string? senha);
}

/// <summary>
/// Emissao local de tokens, usada quando nao ha IdP configurado.
/// </summary>
/// <remarks>
/// A comparacao de login e senha percorre a lista inteira e usa
/// <c>CryptographicOperations.FixedTimeEquals</c>, pelo mesmo motivo da
/// ApiKey do webhook: sair mais cedo quando o login nao existe transformaria o
/// tempo de resposta em um oraculo de quais usuarios sao validos.
/// </remarks>
public sealed class ServicoTokenJwt(IOptions<OpcoesAutenticacao> opcoes, TimeProvider relogio) : IServicoTokenJwt
{
    private readonly OpcoesAutenticacao _opcoes = opcoes.Value;

    public TokenEmitido? Autenticar(string? login, string? senha)
    {
        if (!_opcoes.EmissaoLocalHabilitada)
        {
            throw new InvalidOperationException(
                "Emissao local de token desabilitada: a API esta configurada para validar tokens do IdP.");
        }

        var usuario = Localizar(login, senha);
        if (usuario is null)
        {
            return null;
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        var expiracao = agora.AddMinutes(Math.Max(1, _opcoes.ExpiracaoMinutos));

        var credenciais = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opcoes.ChaveAssinatura)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _opcoes.Emissor,
            audience: _opcoes.Audiencia,
            // Claims com os nomes curtos que o token de fato carrega. Usar as URIs
            // de ClaimTypes dependeria do mapa de saida do handler, que reescreve
            // umas e deixa outras passar — e o que passa intacto nao casaria com
            // o RoleClaimType configurado na validacao.
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, usuario.Login),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(OpcoesAutenticacao.ClaimUsuario, usuario.Login),
                new Claim(OpcoesAutenticacao.ClaimNome, string.IsNullOrWhiteSpace(usuario.Nome) ? usuario.Login : usuario.Nome),
                new Claim(OpcoesAutenticacao.ClaimPapel, usuario.Papel),
            ],
            notBefore: agora,
            expires: expiracao,
            signingCredentials: credenciais);

        return new TokenEmitido(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiracao,
            usuario.Login,
            string.IsNullOrWhiteSpace(usuario.Nome) ? usuario.Login : usuario.Nome,
            usuario.Papel);
    }

    private UsuarioPainel? Localizar(string? login, string? senha)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(senha))
        {
            return null;
        }

        var loginRecebido = Encoding.UTF8.GetBytes(login.Trim());
        var senhaRecebida = Encoding.UTF8.GetBytes(senha);

        UsuarioPainel? encontrado = null;

        foreach (var candidato in _opcoes.Usuarios)
        {
            var loginConfere = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(candidato.Login ?? string.Empty),
                loginRecebido);

            var senhaConfere = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(candidato.Senha ?? string.Empty),
                senhaRecebida);

            if (loginConfere && senhaConfere)
            {
                encontrado = candidato;
            }
        }

        return encontrado;
    }
}
