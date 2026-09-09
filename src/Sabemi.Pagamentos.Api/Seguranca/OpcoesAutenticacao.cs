namespace Sabemi.Pagamentos.Api.Seguranca;

/// <summary>
/// Autenticacao do painel administrativo.
/// </summary>
/// <remarks>
/// <para>
/// Ha dois modos, e a configuracao escolhe entre eles. Com
/// <see cref="Authority"/> preenchido, a API vira apenas validadora de tokens do
/// IdP corporativo — que e como isto roda em producao, com SSO de verdade e sem
/// nenhuma senha do nosso lado. Sem <see cref="Authority"/>, a propria API emite
/// o token a partir da lista de <see cref="Usuarios"/>, o que existe para que o
/// painel seja demonstravel sem depender de um Keycloak no ar.
/// </para>
/// <para>
/// A lista local guarda senha em claro no appsettings de desenvolvimento, na
/// mesma categoria da ApiKey e do segredo HMAC ja presentes ali: nenhum desses
/// valores vale fora da maquina de quem esta avaliando o projeto. Em producao a
/// secao inteira e substituida pelo <see cref="Authority"/>.
/// </para>
/// </remarks>
public sealed class OpcoesAutenticacao
{
    public const string Secao = "Autenticacao";

    public const string PapelOperador = "operador";
    public const string PapelAdministrador = "administrador";

    /// <summary>
    /// Nomes das claims no token, sem o mapeamento legado para URIs de WS-Federation.
    /// </summary>
    /// <remarks>
    /// O handler roda com <c>MapInboundClaims = false</c>, entao a claim chega com
    /// o nome curto que o JWT carrega. Usar <c>ClaimTypes.Role</c> no codigo
    /// procuraria uma URI que nao existe no token e devolveria sempre nulo.
    /// </remarks>
    public const string ClaimUsuario = "unique_name";

    public const string ClaimPapel = "role";

    public const string ClaimNome = "nome";

    /// <summary>Endereco do IdP corporativo. Preenchido, desliga a emissao local de tokens.</summary>
    public string? Authority { get; set; }

    public bool ExigirHttpsNoMetadata { get; set; } = true;

    public string Emissor { get; set; } = "sabemi-pagamentos";

    public string Audiencia { get; set; } = "sabemi-painel";

    /// <summary>Chave de assinatura HS256. Em producao vem de cofre, nunca do appsettings.</summary>
    public string ChaveAssinatura { get; set; } = string.Empty;

    public int ExpiracaoMinutos { get; set; } = 60;

    public IList<UsuarioPainel> Usuarios { get; init; } = [];

    public bool EmissaoLocalHabilitada => string.IsNullOrWhiteSpace(Authority);
}

public sealed class UsuarioPainel
{
    public string Login { get; set; } = string.Empty;

    public string Senha { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    /// <summary>
    /// <c>operador</c> consulta; <c>administrador</c> tambem reprocessa a DLQ e
    /// le a trilha de auditoria — quem audita nao pode ser auditado so por si mesmo.
    /// </summary>
    public string Papel { get; set; } = OpcoesAutenticacao.PapelOperador;
}
