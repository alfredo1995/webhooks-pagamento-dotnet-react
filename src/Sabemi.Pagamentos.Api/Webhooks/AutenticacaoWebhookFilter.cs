using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Sabemi.Pagamentos.Application.Webhooks;

namespace Sabemi.Pagamentos.Api.Webhooks;

/// <summary>
/// Autentica a notificacao do banco antes de qualquer processamento.
/// </summary>
/// <remarks>
/// <para>
/// Sao duas verificacoes: a <c>X-Api-Key</c> diz <i>quem</i> esta chamando e a
/// <c>X-Signature</c> prova que o corpo nao foi adulterado e que o chamador
/// conhece o segredo compartilhado. So a ApiKey seria fraca — ela viaja inteira
/// em todo request e vaza em qualquer log de header mal configurado.
/// </para>
/// <para>
/// Durante a janela de rotacao as duas geracoes de chave sao aceitas, e o uso da
/// anterior sai no log como aviso. E o que torna a troca de segredo uma operacao
/// rotineira em vez de uma parada combinada com o parceiro.
/// </para>
/// <para>
/// Requisicao nao autenticada nao e gravada no log de eventos: o log existe para
/// auditar o parceiro, e aceitar corpo de origem desconhecida transformaria a
/// tabela em vetor de inundacao.
/// </para>
/// </remarks>
public sealed partial class AutenticacaoWebhookFilter(
    IOptions<OpcoesWebhook> opcoes,
    TimeProvider relogio,
    ILogger<AutenticacaoWebhookFilter> logger) : IAsyncResourceFilter
{
    public const string HeaderApiKey = "X-Api-Key";
    public const string HeaderAssinatura = "X-Signature";
    public const string ItemPayloadBruto = "webhook.payload-bruto";
    public const string ItemParceiro = "webhook.parceiro";

    private readonly OpcoesWebhook _opcoes = opcoes.Value;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var request = context.HttpContext.Request;

        // O corpo precisa ser lido cru para a assinatura e para o log de eventos brutos.
        request.EnableBuffering();
        string corpo;
        using (var leitor = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true))
        {
            corpo = await leitor.ReadToEndAsync(context.HttpContext.RequestAborted);
        }

        request.Body.Position = 0;

        var agora = relogio.GetUtcNow().UtcDateTime;
        var apiKey = request.Headers[HeaderApiKey].ToString();
        var (parceiro, apiKeyAnterior) = LocalizarParceiro(apiKey, agora);

        if (parceiro is null)
        {
            LogApiKeyInvalida(logger, context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido");
            context.Result = Recusar("ApiKey ausente ou invalida.");

            return;
        }

        if (apiKeyAnterior)
        {
            LogChaveAnterior(logger, parceiro.Nome, "ApiKey", parceiro.ChavesAnterioresValidasAte ?? agora);
        }

        if (_opcoes.ExigirAssinatura)
        {
            var assinatura = request.Headers[HeaderAssinatura].ToString();
            var confere = CalculadoraAssinatura.Conferir(corpo, parceiro.Segredo, assinatura);

            if (!confere
                && parceiro.JanelaDeRotacaoAberta(agora)
                && !string.IsNullOrEmpty(parceiro.SegredoAnterior)
                && CalculadoraAssinatura.Conferir(corpo, parceiro.SegredoAnterior, assinatura))
            {
                confere = true;
                LogChaveAnterior(logger, parceiro.Nome, "Segredo", parceiro.ChavesAnterioresValidasAte ?? agora);
            }

            if (!confere)
            {
                LogAssinaturaInvalida(logger, parceiro.Nome);
                context.Result = Recusar("Assinatura invalida para o corpo enviado.");

                return;
            }
        }

        context.HttpContext.Items[ItemPayloadBruto] = corpo;
        context.HttpContext.Items[ItemParceiro] = parceiro.Nome;

        await next();
    }

    /// <summary>
    /// Encontra o parceiro dono da ApiKey, aceitando a chave anterior enquanto a
    /// janela de rotacao estiver aberta.
    /// </summary>
    /// <remarks>
    /// Percorre todos os parceiros mesmo depois de achar, e compara em tempo
    /// constante: sem isso, o tempo de resposta revelaria a posicao da chave na
    /// lista e quantos caracteres dela estao corretos.
    /// </remarks>
    private (ParceiroWebhook? Parceiro, bool UsouChaveAnterior) LocalizarParceiro(string apiKeyRecebida, DateTime agoraUtc)
    {
        if (string.IsNullOrWhiteSpace(apiKeyRecebida))
        {
            return (null, false);
        }

        var recebida = Encoding.UTF8.GetBytes(apiKeyRecebida.Trim());

        ParceiroWebhook? encontrado = null;
        var usouAnterior = false;

        foreach (var parceiro in _opcoes.Parceiros)
        {
            if (Confere(parceiro.ApiKey, recebida))
            {
                encontrado = parceiro;
                usouAnterior = false;

                continue;
            }

            if (parceiro.JanelaDeRotacaoAberta(agoraUtc) && Confere(parceiro.ApiKeyAnterior, recebida))
            {
                encontrado = parceiro;
                usouAnterior = true;
            }
        }

        return (encontrado, usouAnterior);
    }

    private static bool Confere(string? esperada, byte[] recebida)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(esperada ?? string.Empty), recebida);

    private static ObjectResult Recusar(string detalhe) => new(new ProblemDetails
    {
        Status = StatusCodes.Status401Unauthorized,
        Title = "Nao autorizado.",
        Detail = detalhe,
    })
    {
        StatusCode = StatusCodes.Status401Unauthorized,
    };

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "Webhook recusado: ApiKey invalida. origem={Origem}")]
    private static partial void LogApiKeyInvalida(ILogger logger, string origem);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning,
        Message = "Webhook recusado: assinatura invalida. parceiro={Parceiro}")]
    private static partial void LogAssinaturaInvalida(ILogger logger, string parceiro);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning,
        Message = "Parceiro ainda usa a chave anterior. parceiro={Parceiro} chave={Chave} janela_ate={JanelaAte:O}")]
    private static partial void LogChaveAnterior(ILogger logger, string parceiro, string chave, DateTime janelaAte);
}
