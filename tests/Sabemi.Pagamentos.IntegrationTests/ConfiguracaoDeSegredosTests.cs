using System.Text.Json;
using FluentAssertions;
using Sabemi.Pagamentos.Api.Seguranca;
using Sabemi.Pagamentos.Api.Webhooks;
using Xunit;

namespace Sabemi.Pagamentos.IntegrationTests;

/// <summary>
/// Trava o combinado de que nenhum segredo mora em arquivo versionado, e de que
/// a falta de um deles derruba a subida com o nome da chave que falta.
/// </summary>
/// <remarks>
/// O primeiro teste e o que impede a regressao silenciosa: nada no compilador
/// impede alguem de "resolver" um erro de configuracao colando a senha de volta
/// no <c>appsettings.json</c>, e o commit passaria batido em qualquer review
/// apressado. Aqui isso vira teste vermelho.
/// </remarks>
public class ConfiguracaoDeSegredosTests
{
    private static readonly string[] CaminhosDeSegredo =
    [
        "Autenticacao:ChaveAssinatura",
        "Autenticacao:Usuarios:*:Senha",
        "Webhook:Parceiros:*:ApiKey",
        "Webhook:Parceiros:*:Segredo",
        "Processamento:RabbitMq:Senha",
        "ConnectionStrings:SqlServer",
    ];

    [Fact]
    public void AppsettingsVersionado_naoCarregaNenhumSegredoPreenchido()
    {
        var caminho = LocalizarAppsettings();
        using var documento = JsonDocument.Parse(File.ReadAllText(caminho));

        var preenchidos = CaminhosDeSegredo
            .SelectMany(caminhoDeSegredo => ValoresEm(documento.RootElement, caminhoDeSegredo))
            .Where(valor => !string.IsNullOrWhiteSpace(valor.Valor))
            .Select(valor => $"{valor.Caminho} = '{valor.Valor}'")
            .ToArray();

        preenchidos.Should().BeEmpty(
            "segredo em arquivo versionado vaza no primeiro clone; o valor deve vir "
            + "do .env (compose) ou do user-secrets (dotnet run local)");
    }

    [Fact]
    public void Webhook_semApiKeyConfigurada_naoSobe()
    {
        var opcoes = new OpcoesWebhook
        {
            ExigirAssinatura = true,
            Parceiros = { new ParceiroWebhook { Nome = "banco", ApiKey = "", Segredo = "s" } },
        };

        var resultado = new ValidacaoOpcoesWebhook().Validate(null, opcoes);

        resultado.Failed.Should().BeTrue();
        resultado.Failures.Should().ContainMatch("*Webhook:Parceiros:0:ApiKey*WEBHOOK_API_KEY*");
    }

    [Fact]
    public void Webhook_semSegredo_soReclamaQuandoAssinaturaEExigida()
    {
        var opcoes = new OpcoesWebhook
        {
            ExigirAssinatura = false,
            Parceiros = { new ParceiroWebhook { Nome = "banco", ApiKey = "k", Segredo = "" } },
        };

        new ValidacaoOpcoesWebhook().Validate(null, opcoes).Succeeded.Should().BeTrue();

        opcoes.ExigirAssinatura = true;

        new ValidacaoOpcoesWebhook().Validate(null, opcoes).Failed.Should().BeTrue();
    }

    [Fact]
    public void Painel_comChaveCurta_naoSobe()
    {
        var opcoes = new OpcoesAutenticacao
        {
            ChaveAssinatura = "curta-demais",
            Usuarios = { new UsuarioPainel { Login = "admin", Senha = "x", Papel = "administrador" } },
        };

        var resultado = new ValidacaoOpcoesAutenticacao().Validate(null, opcoes);

        resultado.Failed.Should().BeTrue();
        resultado.Failures.Should().ContainMatch("*HS256 exige ao menos 32*");
    }

    [Fact]
    public void Painel_comPapelInexistente_naoSobe()
    {
        var opcoes = new OpcoesAutenticacao
        {
            ChaveAssinatura = new string('k', 32),
            Usuarios = { new UsuarioPainel { Login = "admin", Senha = "x", Papel = "supervisor" } },
        };

        new ValidacaoOpcoesAutenticacao().Validate(null, opcoes).Failures
            .Should().ContainMatch("*'supervisor' nao existe*");
    }

    /// <summary>
    /// Com IdP configurado quem emite token e ele: cobrar chave e usuarios locais
    /// impediria justamente a configuracao de producao.
    /// </summary>
    [Fact]
    public void Painel_comAuthority_dispensaChaveEUsuariosLocais()
    {
        var opcoes = new OpcoesAutenticacao { Authority = "https://idp.sabemi.local" };

        new ValidacaoOpcoesAutenticacao().Validate(null, opcoes).Succeeded.Should().BeTrue();
    }

    private static IEnumerable<(string Caminho, string? Valor)> ValoresEm(JsonElement raiz, string caminho)
    {
        var partes = caminho.Split(':');
        var atuais = new List<(string Caminho, JsonElement Elemento)> { (string.Empty, raiz) };

        foreach (var parte in partes)
        {
            var proximos = new List<(string, JsonElement)>();

            foreach (var (prefixo, elemento) in atuais)
            {
                // O curinga cobre listas cujo tamanho e configuravel: um parceiro
                // novo com segredo colado no arquivo tem que cair no mesmo teste.
                if (parte == "*")
                {
                    if (elemento.ValueKind is JsonValueKind.Array)
                    {
                        proximos.AddRange(elemento.EnumerateArray()
                            .Select((item, i) => ($"{prefixo}:{i}", item)));
                    }

                    continue;
                }

                if (elemento.ValueKind is JsonValueKind.Object
                    && elemento.TryGetProperty(parte, out var filho))
                {
                    proximos.Add(($"{prefixo}:{parte}", filho));
                }
            }

            atuais = proximos;
        }

        return atuais.Select(a => (a.Caminho.TrimStart(':'), a.Elemento.ValueKind switch
        {
            JsonValueKind.String => a.Elemento.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => a.Elemento.ToString(),
        }));
    }

    /// <summary>
    /// Sobe a partir do diretorio do teste ate achar a solucao: o caminho relativo
    /// a partir de bin/ muda entre configuracoes de build e entre runners.
    /// </summary>
    private static string LocalizarAppsettings()
    {
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);

        while (diretorio is not null && !File.Exists(Path.Combine(diretorio.FullName, "SabemiPagamentos.sln")))
        {
            diretorio = diretorio.Parent;
        }

        diretorio.Should().NotBeNull("o teste precisa achar a raiz do repositorio");

        var caminho = Path.Combine(
            diretorio!.FullName, "src", "Sabemi.Pagamentos.Api", "appsettings.json");

        File.Exists(caminho).Should().BeTrue();

        return caminho;
    }
}
