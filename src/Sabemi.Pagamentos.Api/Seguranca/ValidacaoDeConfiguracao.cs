using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Api.Webhooks;

namespace Sabemi.Pagamentos.Api.Seguranca;

/// <summary>
/// Valida, na subida da aplicacao, que os segredos realmente chegaram.
/// </summary>
/// <remarks>
/// <para>
/// Nenhum segredo mora no <c>appsettings.json</c>: eles vem do ambiente (o
/// <c>.env</c> do compose) ou do user-secrets no desenvolvimento local. O preco
/// disso e que um segredo faltando deixa de ser impossivel — e sem validacao a
/// aplicacao subiria com string vazia e falharia bem longe da causa: o login do
/// painel estourando ao assinar com chave de tamanho zero, o parceiro tomando
/// <c>401</c> sem motivo aparente.
/// </para>
/// <para>
/// Por isso a checagem acontece em <c>ValidateOnStart</c> e nao no primeiro uso:
/// configuracao incompleta e erro de implantacao, e erro de implantacao deve
/// aparecer no deploy, nao na primeira requisicao do parceiro.
/// </para>
/// </remarks>
public sealed class ValidacaoOpcoesWebhook : IValidateOptions<OpcoesWebhook>
{
    public ValidateOptionsResult Validate(string? name, OpcoesWebhook options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var falhas = new List<string>();

        if (options.Parceiros.Count == 0)
        {
            falhas.Add("Webhook:Parceiros esta vazio — nenhum parceiro poderia se autenticar.");
        }

        for (var i = 0; i < options.Parceiros.Count; i++)
        {
            var parceiro = options.Parceiros[i];

            if (string.IsNullOrWhiteSpace(parceiro.Nome))
            {
                falhas.Add($"Webhook:Parceiros:{i}:Nome nao foi informado.");
            }

            if (string.IsNullOrWhiteSpace(parceiro.ApiKey))
            {
                falhas.Add(Faltando($"Webhook:Parceiros:{i}:ApiKey", "WEBHOOK_API_KEY"));
            }

            // Sem exigir assinatura o segredo nao e usado; cobrar seria travar o
            // modo de teste manual que a propria opcao existe para permitir.
            if (options.ExigirAssinatura && string.IsNullOrWhiteSpace(parceiro.Segredo))
            {
                falhas.Add(Faltando($"Webhook:Parceiros:{i}:Segredo", "WEBHOOK_SEGREDO"));
            }
        }

        return falhas.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(falhas);
    }

    internal static string Faltando(string chave, string variavel)
        => $"{chave} nao foi configurado. Defina {variavel} no .env (compose) ou "
           + $"rode ./tools/configurar-segredos-locais.sh para o dotnet run local.";
}

/// <summary>
/// Valida a autenticacao do painel, respeitando os dois modos possiveis.
/// </summary>
/// <remarks>
/// Com <c>Authority</c> preenchido quem emite token e o IdP, entao chave de
/// assinatura e lista de usuarios nao sao apenas dispensaveis: cobra-las
/// impediria justamente a configuracao de producao. As exigencias abaixo valem
/// so para o modo de emissao local.
/// </remarks>
public sealed class ValidacaoOpcoesAutenticacao : IValidateOptions<OpcoesAutenticacao>
{
    /// <summary>HS256 assina com a chave crua; abaixo de 256 bits o handler recusa.</summary>
    private const int TamanhoMinimoDaChave = 32;

    private static readonly string[] PapeisValidos =
        [OpcoesAutenticacao.PapelOperador, OpcoesAutenticacao.PapelAdministrador];

    public ValidateOptionsResult Validate(string? name, OpcoesAutenticacao options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.EmissaoLocalHabilitada)
        {
            return ValidateOptionsResult.Success;
        }

        var falhas = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ChaveAssinatura))
        {
            falhas.Add(ValidacaoOpcoesWebhook.Faltando("Autenticacao:ChaveAssinatura", "JWT_CHAVE_ASSINATURA"));
        }
        else if (options.ChaveAssinatura.Length < TamanhoMinimoDaChave)
        {
            falhas.Add(
                $"Autenticacao:ChaveAssinatura tem {options.ChaveAssinatura.Length} caracteres; "
                + $"HS256 exige ao menos {TamanhoMinimoDaChave}.");
        }

        if (options.Usuarios.Count == 0)
        {
            falhas.Add(
                "Autenticacao:Usuarios esta vazio e nao ha Authority configurada — "
                + "ninguem conseguiria entrar no painel.");
        }

        for (var i = 0; i < options.Usuarios.Count; i++)
        {
            var usuario = options.Usuarios[i];

            if (string.IsNullOrWhiteSpace(usuario.Login))
            {
                falhas.Add($"Autenticacao:Usuarios:{i}:Login nao foi informado.");
            }

            if (string.IsNullOrWhiteSpace(usuario.Senha))
            {
                falhas.Add(ValidacaoOpcoesWebhook.Faltando(
                    $"Autenticacao:Usuarios:{i}:Senha",
                    "PAINEL_ADMIN_SENHA / PAINEL_OPERADOR_SENHA"));
            }

            if (!PapeisValidos.Contains(usuario.Papel, StringComparer.OrdinalIgnoreCase))
            {
                falhas.Add(
                    $"Autenticacao:Usuarios:{i}:Papel = '{usuario.Papel}' nao existe. "
                    + $"Use um entre: {string.Join(", ", PapeisValidos)}.");
            }
        }

        return falhas.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(falhas);
    }
}
