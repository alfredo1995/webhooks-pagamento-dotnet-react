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
/// Requisicao nao autenticada nao e gravada no log de eventos: o log existe para
/// auditar o parceiro, e aceitar corpo de origem desconhecida transformaria a
/// tabela em vetor de inundacao.
/// </para>
/// </remarks>
public sealed partial class AutenticacaoWebhookFilter(
    IOptions<OpcoesWebhook> opcoes,
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

        var apiKey = request.Headers[HeaderApiKey].ToString();
        var parceiro = LocalizarParceiro(apiKey);

        if (parceiro is null)
        {
            LogApiKeyInvalida(logger, context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido");
            context.Result = Recusar("ApiKey ausente ou invalida.");

            return;
        }

        if (_opcoes.ExigirAssinatura)
        {
            var assinatura = request.Headers[HeaderAssinatura].ToString();

            if (!CalculadoraAssinatura.Conferir(corpo, parceiro.Segredo, assinatura))
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

    private ParceiroWebhook? LocalizarParceiro(string apiKeyRecebida)
    {
        if (string.IsNullOrWhiteSpace(apiKeyRecebida))
        {
            return null;
        }

        var recebida = Encoding.UTF8.GetBytes(apiKeyRecebida.Trim());

        // Percorre todos os parceiros mesmo apos achar: evita que o tempo de
        // resposta revele a posicao da chave na lista.
        ParceiroWebhook? encontrado = null;
        foreach (var parceiro in _opcoes.Parceiros)
        {
            var esperada = Encoding.UTF8.GetBytes(parceiro.ApiKey ?? string.Empty);

            if (CryptographicOperations.FixedTimeEquals(esperada, recebida))
            {
                encontrado = parceiro;
            }
        }

        return encontrado;
    }

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
}
